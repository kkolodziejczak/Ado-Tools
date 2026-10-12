[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [ValidateSet('AdoRead.Tool', 'AdoRead.PullRequests.Tool', 'AdoRead.WorkItems.Tool', 'AdoRead.Wiki.Tool', 'AdoRead.Sprints.Tool')]
    [string]$PackageId,

    [Parameter(Mandatory)]
    [ValidateSet('ado-read', 'ado-read-pr', 'ado-read-work-item', 'ado-read-wiki', 'ado-read-sprints')]
    [string]$CommandName,

    [string]$PackageVersion = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Commands each package must list under its help, and the foreign command it must reject.
$toolContracts = @{
    'AdoRead.Tool' = @{
        Command = 'ado-read'
        Commands = @('config', 'auth', 'privacy', 'projects', 'teams', 'pr', 'work-item', 'wiki', 'sprints')
        Foreign = $null
    }
    'AdoRead.PullRequests.Tool' = @{
        Command = 'ado-read-pr'
        Commands = @('config', 'auth', 'privacy', 'projects', 'teams')
        Foreign = @('wiki', 'list')
    }
    'AdoRead.WorkItems.Tool' = @{
        Command = 'ado-read-work-item'
        Commands = @('config', 'auth', 'privacy', 'projects', 'teams')
        Foreign = @('wiki', 'list')
    }
    'AdoRead.Wiki.Tool' = @{
        Command = 'ado-read-wiki'
        Commands = @('config', 'auth', 'privacy', 'projects', 'teams', 'list', 'pages', 'page', 'search')
        Foreign = @('pr', '1')
    }
    'AdoRead.Sprints.Tool' = @{
        Command = 'ado-read-sprints'
        Commands = @('config', 'auth', 'privacy', 'projects', 'teams', 'list', 'show')
        Foreign = @('wiki', 'list')
    }
}

$contract = $toolContracts[$PackageId]
if ($contract.Command -ne $CommandName)
{
    throw "Package $PackageId exposes the command $($contract.Command), not $CommandName."
}

$isUmbrella = $PackageId -eq 'AdoRead.Tool'
$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$packageDirectory = Split-Path -Parent $packageFullPath
$expectedPackageName = "$PackageId.$PackageVersion.nupkg"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ado-read-package-$([Guid]::NewGuid().ToString('N'))"
$toolPath = Join-Path $temporaryRoot 'tool'
$configPath = Join-Path $temporaryRoot 'config.json'
$toolExecutable = Join-Path $toolPath "$CommandName.exe"

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

function Get-ListedCommands
{
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string[]]$HelpLines
    )

    $names = [System.Collections.Generic.List[string]]::new()
    $inCommandSection = $false
    foreach ($line in $HelpLines)
    {
        if ($line -eq 'Commands:')
        {
            $inCommandSection = $true
            continue
        }

        if (-not $inCommandSection)
        {
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line))
        {
            break
        }

        $names.Add(($line.Trim() -split '\s+')[0])
    }

    return , $names.ToArray()
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

function Test-InstalledTool
{
    New-Item -ItemType Directory -Path $toolPath -Force | Out-Null
    & dotnet tool install --tool-path $toolPath --add-source $packageDirectory $PackageId --version $PackageVersion --ignore-failed-sources
    if ($LASTEXITCODE -ne 0)
    {
        throw "Could not install the produced $PackageId package."
    }

    $installedTools = & dotnet tool list --tool-path $toolPath
    if ($LASTEXITCODE -ne 0 -or ($installedTools -join [Environment]::NewLine) -notmatch "(?im)^$([regex]::Escape($PackageId))\s+$([regex]::Escape($PackageVersion))\s+")
    {
        throw "Installed $PackageId package did not report version $PackageVersion."
    }

    if (-not (Test-Path -LiteralPath $toolExecutable -PathType Leaf))
    {
        throw "Installed tool executable was not found at $toolExecutable."
    }
}

function Test-InstalledHelp
{
    $help = @(& $toolExecutable --help)
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installed $CommandName --help exited with code $LASTEXITCODE."
    }

    $listed = Get-ListedCommands -HelpLines $help
    $expected = @($contract.Commands | Sort-Object)
    $actual = @($listed | Sort-Object)
    if (($expected -join ',') -ne ($actual -join ','))
    {
        throw "Installed $CommandName help listed '$($actual -join ', ')' instead of '$($expected -join ', ')'."
    }
}

function Test-InstalledConfiguration
{
    $initialized = & $toolExecutable config init --organization https://dev.azure.com/fabricated --config $configPath --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installed $CommandName could not initialize isolated configuration."
    }

    $configuration = & $toolExecutable config show --config $configPath --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installed $CommandName could not read isolated configuration."
    }

    $configurationDocument = ($configuration -join [Environment]::NewLine) | ConvertFrom-Json
    if ($configurationDocument.status -ne 'success' -or $configurationDocument.results[0].organizationUrl -ne 'https://dev.azure.com/fabricated/')
    {
        throw "Installed $CommandName returned an unexpected configuration document."
    }

    # No privacy policy is stored for the fabricated organization on a clean machine, so a read must refuse
    # before it touches a token, and privacy show must report that nothing is configured.
    Assert-JsonValidationError -Arguments @('projects', '--config', $configPath, '--compact') -ExpectedCode 'privacyPolicyRequired'

    $privacy = & $toolExecutable privacy show --config $configPath --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Installed $CommandName could not show its privacy policy."
    }

    $privacyDocument = ($privacy -join [Environment]::NewLine) | ConvertFrom-Json
    if ($privacyDocument.status -ne 'success' -or $privacyDocument.results[0].configured)
    {
        throw "Installed $CommandName reported a privacy policy on a clean machine."
    }
}

function Test-SliceSmoke
{
    Test-InstalledTool
    Test-InstalledHelp
    Test-InstalledConfiguration
    Assert-JsonValidationError -Arguments ([string[]]$contract.Foreign + @('--compact')) -ExpectedCode 'invalidArguments'
}

function Test-UmbrellaSmoke
{
    Test-InstalledTool

    $workItemHelp = @(& $toolExecutable work-item --help)
    if ($LASTEXITCODE -ne 0 -or ($workItemHelp -join [Environment]::NewLine) -notmatch '(?i)--download-images')
    {
        throw 'Installed work-item help omitted --download-images.'
    }

    $invalidImageDirectory = Join-Path $temporaryRoot 'image-output-file'
    New-Item -ItemType File -Path $invalidImageDirectory -Force | Out-Null
    $installedCoreAssembly = Get-ChildItem -LiteralPath $toolPath -Recurse -Filter 'AdoRead.Core.dll' | Select-Object -First 1
    if ($null -eq $installedCoreAssembly)
    {
        throw 'Installed ado-read package did not contain AdoRead.Core.dll.'
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
        "--property:AdoReadCoreAssemblyPath=$($installedCoreAssembly.FullName)",
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

    $script:fixtureHostAssembly = Join-Path $fakeHostOutput 'Release\net10.0\AdoRead.FakeHost.dll'
    $fixtureCoreAssembly = Join-Path $fakeHostOutput 'Release\net10.0\AdoRead.Core.dll'
    if (-not (Test-Path -LiteralPath $fixtureHostAssembly -PathType Leaf) -or -not (Test-Path -LiteralPath $fixtureCoreAssembly -PathType Leaf))
    {
        throw 'Fabricated E2E host output did not contain the installed package assembly.'
    }

    Get-ChildItem -LiteralPath (Split-Path -Parent $installedCoreAssembly.FullName) -Filter '*.dll' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Split-Path -Parent $fixtureHostAssembly) -Force }

    if ((Get-FileHash -LiteralPath $installedCoreAssembly.FullName).Hash -ne (Get-FileHash -LiteralPath $fixtureCoreAssembly).Hash)
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

    # Withheld images must not be downloaded or written, and the privacy section must say so.
    $withheldOutputDirectory = Join-Path $temporaryRoot 'withheld-image-output'
    $withheldOutput = & dotnet $fixtureHostAssembly work-item 123 --download-images $withheldOutputDirectory --withhold images --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Fixture withheld-image command failed with exit code $LASTEXITCODE."
    }

    $withheld = ($withheldOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $withheldFields = @($withheld.results[0].PSObject.Properties.Name)
    if (($withheld.status -ne 'success') -or ($withheld.schemaVersion -ne '2.0') -or ($withheld.privacy.withheld -notcontains 'images') -or ($withheldFields -contains 'media') -or (Test-Path -LiteralPath $withheldOutputDirectory))
    {
        throw 'Fixture withheld-image command downloaded or wrote images, or did not report them as withheld.'
    }

    # The fixture host has a full policy, so an invalid image directory is reported as before.
    $invalidImageOutput = & dotnet $fixtureHostAssembly work-item 123 --download-images $invalidImageDirectory --compact
    if ($LASTEXITCODE -ne 2)
    {
        throw "Fixture invalid image directory exited with code $LASTEXITCODE instead of 2."
    }

    $invalidImageDocument = ($invalidImageOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($invalidImageDocument.errors[0].code -ne 'invalidImageOutputDirectory')
    {
        throw 'Fixture invalid image directory did not report invalidImageOutputDirectory.'
    }

    Test-InstalledHelp
    Test-InstalledConfiguration

    # A clean machine has no privacy policy, so the read refuses before it looks at the image directory.
    Assert-JsonValidationError -Arguments @(
        'work-item',
        '123',
        '--download-images',
        $invalidImageDirectory,
        '--config',
        $configPath,
        '--compact') -ExpectedCode 'privacyPolicyRequired'

    Assert-JsonValidationError -Arguments @('work-item', '0', '--config', $configPath, '--compact') -ExpectedCode 'invalidWorkItemId'
    Assert-JsonValidationError -Arguments @('pr', '0', '--config', $configPath, '--compact') -ExpectedCode 'invalidPullRequestId'
    Invoke-FixtureCommand -Arguments @('work-item', 'https://dev.azure.com/fabricated/FabricatedApp/_workitems/edit/123', '--compact') -ExpectedKind 'workItemSearch' -ExpectedId 123 -ExpectedSchemaVersion '2.0'
    Invoke-FixtureCommand -Arguments @('pr', 'https://dev.azure.com/fabricated/Fabricated.Alpha/_git/Fabricated.Alpha.Repository/pullrequest/126', '--compact') -ExpectedKind 'pullRequestSearch' -ExpectedId 126
    Invoke-FixtureCommand -Arguments @('wiki', 'list', '--compact') -ExpectedKind 'wikiCatalog'
    Invoke-FixtureCommand -Arguments @('wiki', 'pages', '--wiki', 'FabricatedApp.wiki', '--compact') -ExpectedKind 'wikiPageCatalog'
    Invoke-FixtureCommand -Arguments @('wiki', 'page', 'https://dev.azure.com/fabricated/FabricatedApp/_wiki/wikis/FabricatedApp.wiki/51/Authentication', '--compact') -ExpectedKind 'wikiPageLookup' -ExpectedId 51
    $searchOutput = & dotnet $fixtureHostAssembly wiki search authentication --compact
    if ($LASTEXITCODE -ne 0)
    {
        throw "Fixture wiki search failed with exit code $LASTEXITCODE."
    }

    $search = ($searchOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($search.status -ne 'success' -or $search.kind -ne 'wikiFullTextSearch' -or $search.results.Count -ne 1)
    {
        throw 'Fixture wiki search returned an unexpected success document.'
    }

    Invoke-FixtureCommand -Arguments (@('wiki', 'page') + [string[]]$search.results[0].next.arguments + @('--compact')) -ExpectedKind 'wikiPageLookup'
}

try
{
    if ([System.IO.Path]::GetFileName($packageFullPath) -ne $expectedPackageName)
    {
        throw "Expected package '$expectedPackageName', received '$([System.IO.Path]::GetFileName($packageFullPath))'."
    }

    if ($isUmbrella)
    {
        Test-UmbrellaSmoke
    }
    else
    {
        Test-SliceSmoke
    }
}
finally
{
    if (Test-Path -LiteralPath $temporaryRoot)
    {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Installed package smoke test passed: $PackageId $PackageVersion ($CommandName)."
exit 0
