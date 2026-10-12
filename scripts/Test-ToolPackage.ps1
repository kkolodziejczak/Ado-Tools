[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PackagePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$packageDirectory = Split-Path -Parent $packageFullPath
$expectedPackageName = 'AdoRead.Tool.0.5.0.nupkg'
$expectedPackageVersion = '0.5.0'
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ado-read-package-$([Guid]::NewGuid().ToString('N'))"
$toolPath = Join-Path $temporaryRoot 'tool'
$configPath = Join-Path $temporaryRoot 'config.json'

function Assert-JsonValidationError
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$ExpectedCode
    )

    $output = & $toolExecutable @Arguments
    if ($LASTEXITCODE -ne 2)
    {
        throw "Expected exit code 2 for '$($Arguments -join ' ')', received $LASTEXITCODE."
    }

    $document = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if ($document.status -ne 'error' -or $document.errors[0].code -ne $ExpectedCode)
    {
        throw "Expected JSON error '$ExpectedCode' for '$($Arguments -join ' ')'."
    }
}

function Invoke-FixtureCommand
{
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,

        [Parameter(Mandatory)]
        [string]$ExpectedKind,

        [int]$ExpectedId = -1,

        [string]$ExpectedSchemaVersion = ''
    )

    $output = & dotnet $fixtureHostAssembly @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "Fixture command '$($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }

    $document = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    $schemaMatches = -not $ExpectedSchemaVersion -or $document.schemaVersion -eq $ExpectedSchemaVersion
    if (($document.status -ne 'success') -or ($document.kind -ne $ExpectedKind) -or ($ExpectedId -ge 0 -and $document.results[0].id -ne $ExpectedId) -or -not $schemaMatches)
    {
        throw "Fixture command '$($Arguments -join ' ')' returned an unexpected success document."
    }
}

function Invoke-FixtureMediaCommand
{
    param(
        [Parameter(Mandatory)]
        [string]$OutputDirectory
    )

    $output = @(& dotnet $fixtureHostAssembly work-item 123 --download-images $OutputDirectory --compact)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Fixture media command failed with exit code $LASTEXITCODE."
    }

    if ($output.Count -ne 1)
    {
        throw "Fixture media command emitted $($output.Count) stdout lines instead of one JSON document."
    }

    $document = $output[0] | ConvertFrom-Json
    $expectedDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (($document.status -ne 'success') -or ($document.kind -ne 'workItemSearch') -or ($document.schemaVersion -ne '2.1') -or ($document.query.downloadImagesDirectory -ne $expectedDirectory))
    {
        throw 'Fixture media command returned an unexpected schema 2.1 document.'
    }

    $items = @($document.results[0].media.items | Where-Object { $_.status -eq 'downloaded' })
    if ($items.Count -ne 1 -or -not (Test-Path -LiteralPath $items[0].localPath -PathType Leaf))
    {
        throw 'Fixture media command did not produce one downloaded image file.'
    }

    return $document
}

try
{
    if ([System.IO.Path]::GetFileName($packageFullPath) -ne $expectedPackageName)
    {
        throw "Expected package '$expectedPackageName', received '$([System.IO.Path]::GetFileName($packageFullPath))'."
    }

    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
    & dotnet tool install --tool-path $toolPath --add-source $packageDirectory AdoRead.Tool --version $expectedPackageVersion --ignore-failed-sources
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not install the produced ado-read package.'
    }

    $installedTools = & dotnet tool list --tool-path $toolPath
    if ($LASTEXITCODE -ne 0 -or ($installedTools -join [Environment]::NewLine) -notmatch "(?im)^AdoRead\.Tool\s+$([regex]::Escape($expectedPackageVersion))\s+")
    {
        throw "Installed ado-read package did not report version $expectedPackageVersion."
    }

    $toolExecutable = Join-Path $toolPath 'ado-read.exe'
    if (-not (Test-Path -LiteralPath $toolExecutable -PathType Leaf))
    {
        throw "Installed tool executable was not found at $toolExecutable."
    }

    $workItemHelp = @(& $toolExecutable work-item --help)
    if ($LASTEXITCODE -ne 0 -or ($workItemHelp -join [Environment]::NewLine) -notmatch '(?i)--download-images')
    {
        throw 'Installed work-item help omitted --download-images.'
    }

    $invalidImageDirectory = Join-Path $temporaryRoot 'image-output-file'
    New-Item -ItemType File -Path $invalidImageDirectory -Force | Out-Null
    Assert-JsonValidationError -Arguments @(
        'work-item',
        '123',
        '--download-images',
        $invalidImageDirectory,
        '--config',
        $configPath,
        '--compact') -ExpectedCode 'invalidImageOutputDirectory'

    $installedCliAssembly = Get-ChildItem -LiteralPath $toolPath -Recurse -Filter 'AdoRead.Cli.dll' | Select-Object -First 1
    if ($null -eq $installedCliAssembly)
    {
        throw 'Installed ado-read package did not contain AdoRead.Cli.dll.'
    }

    $repositoryToolRoot = Split-Path -Parent $PSScriptRoot
    $fakeHostSource = Join-Path $temporaryRoot 'fake-host-source'
    New-Item -ItemType Directory -Path $fakeHostSource -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryToolRoot 'tests\AdoRead.FakeHost\AdoRead.FakeHost.csproj') -Destination $fakeHostSource
    Copy-Item -LiteralPath (Join-Path $repositoryToolRoot 'tests\AdoRead.FakeHost\Program.cs') -Destination $fakeHostSource
    Copy-Item -LiteralPath (Join-Path $repositoryToolRoot 'tests\AdoRead.FakeHost\WikiFixtures.cs') -Destination $fakeHostSource
    Copy-Item -LiteralPath (Join-Path $repositoryToolRoot 'Directory.Build.props') -Destination $fakeHostSource
    $fakeHostProject = Join-Path $fakeHostSource 'AdoRead.FakeHost.csproj'
    $fakeHostOutput = Join-Path $temporaryRoot 'fake-host'
    $fakeHostIntermediate = Join-Path $temporaryRoot 'fake-host-obj'
    $fakeHostProperties = @(
        "--property:AdoReadCliAssemblyPath=$($installedCliAssembly.FullName)",
        "--property:BaseOutputPath=$fakeHostOutput\",
        "--property:BaseIntermediateOutputPath=$fakeHostIntermediate\",
        "--property:MSBuildProjectExtensionsPath=$fakeHostIntermediate\"
    )
    & dotnet restore $fakeHostProject --locked-mode @fakeHostProperties
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not restore the fabricated E2E host in its isolated intermediate directory.'
    }

    & dotnet build $fakeHostProject --no-restore --configuration Release @fakeHostProperties
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Could not build the fabricated E2E host against the installed package assembly.'
    }

    $fixtureHostAssembly = Join-Path $fakeHostOutput 'Release\net10.0\AdoRead.FakeHost.dll'
    $fixtureCliAssembly = Join-Path $fakeHostOutput 'Release\net10.0\AdoRead.Cli.dll'
    if (-not (Test-Path -LiteralPath $fixtureHostAssembly -PathType Leaf) -or -not (Test-Path -LiteralPath $fixtureCliAssembly -PathType Leaf))
    {
        throw 'Fabricated E2E host output did not contain the installed package assembly.'
    }

    Get-ChildItem -LiteralPath (Split-Path -Parent $installedCliAssembly.FullName) -Filter '*.dll' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Split-Path -Parent $fixtureHostAssembly) -Force }

    if ((Get-FileHash -LiteralPath $installedCliAssembly.FullName).Hash -ne (Get-FileHash -LiteralPath $fixtureCliAssembly).Hash)
    {
        throw 'Fabricated E2E host did not use the installed package assembly.'
    }

    $mediaOutputDirectory = Join-Path $temporaryRoot 'media-output'
    New-Item -ItemType Directory -Path $mediaOutputDirectory -Force | Out-Null
    $mediaDocument = Invoke-FixtureMediaCommand -OutputDirectory $mediaOutputDirectory
    if ($mediaDocument.results[0].media.items[0].format -ne 'png')
    {
        throw 'Installed package media smoke test returned an unexpected image format.'
    }

    $help = & $toolExecutable --help
    $helpText = $help -join [Environment]::NewLine
    $requiredCommands = @('work-item', 'pr', 'wikis', 'wiki-pages', 'wiki-page', 'wiki-search')
    $missingCommands = @($requiredCommands | Where-Object { $helpText -notmatch [regex]::Escape($_) })
    if ($LASTEXITCODE -ne 0 -or $missingCommands.Count -ne 0)
    {
        throw "Installed ado-read package help omitted: $($missingCommands -join ', ')."
    }

    $initialized = & $toolExecutable config init --organization https://dev.azure.com/fabricated --config $configPath --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Installed ado-read package could not initialize isolated configuration.'
    }

    $configuration = & $toolExecutable config show --config $configPath --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw 'Installed ado-read package could not read isolated configuration.'
    }

    $configurationDocument = ($configuration -join [Environment]::NewLine) | ConvertFrom-Json
    if ($configurationDocument.status -ne 'success' -or $configurationDocument.results[0].organizationUrl -ne 'https://dev.azure.com/fabricated/')
    {
        throw 'Installed ado-read package returned an unexpected configuration document.'
    }

    Assert-JsonValidationError -Arguments @('work-item', '0', '--config', $configPath, '--compact') -ExpectedCode 'invalidWorkItemId'
    Assert-JsonValidationError -Arguments @('pr', '0', '--config', $configPath, '--compact') -ExpectedCode 'invalidPullRequestId'
    Invoke-FixtureCommand -Arguments @('work-item', 'https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/123', '--compact') -ExpectedKind 'workItemSearch' -ExpectedId 123 -ExpectedSchemaVersion '2.0'
    Invoke-FixtureCommand -Arguments @('pr', 'https://dev.azure.com/fabricated/Fabricated.Alpha/_git/Fabricated.Alpha.Repository/pullrequest/126', '--compact') -ExpectedKind 'pullRequestSearch' -ExpectedId 126
    Invoke-FixtureCommand -Arguments @('wikis', '--compact') -ExpectedKind 'wikiCatalog'
    Invoke-FixtureCommand -Arguments @('wiki-pages', '--wiki', 'FabricatedApp.wiki', '--compact') -ExpectedKind 'wikiPageCatalog'
    Invoke-FixtureCommand -Arguments @('wiki-page', 'https://dev.azure.com/fabricated/FabricatedApp/_wiki/wikis/FabricatedApp.wiki/51/Authentication', '--compact') -ExpectedKind 'wikiPageLookup' -ExpectedId 51
    $searchOutput = & dotnet $fixtureHostAssembly wiki-search authentication --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Fixture wiki-search failed with exit code $LASTEXITCODE."
    }

    $search = ($searchOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($search.status -ne 'success' -or $search.kind -ne 'wikiFullTextSearch' -or $search.results.Count -ne 1)
    {
        throw 'Fixture wiki-search returned an unexpected success document.'
    }

    Invoke-FixtureCommand -Arguments (@('wiki-page') + [string[]]$search.results[0].next.arguments + @('--compact')) -ExpectedKind 'wikiPageLookup'
}
finally
{
    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host 'Installed package smoke test passed.'
