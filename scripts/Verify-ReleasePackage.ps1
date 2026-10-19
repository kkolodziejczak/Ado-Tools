[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PackagePath,

    [string]$ToolRoot,

    [ValidateSet('AdoRead.Tool', 'AdoRead.PullRequests.Tool', 'AdoRead.WorkItems.Tool', 'AdoRead.Wiki.Tool', 'AdoRead.Sprints.Tool', 'AdoRead.Pipelines.Tool', 'AdoWrite.Tool', 'AdoWrite.PullRequests.Tool', 'AdoWrite.WorkItems.Tool')]
    [string]$PackageId,

    [string]$PackageVersion = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($ToolRoot))
{
    $ToolRoot = Split-Path -Parent $PSScriptRoot
}

$toolRootPath = (Resolve-Path -LiteralPath $ToolRoot).Path
$packageFullPath = (Resolve-Path -LiteralPath $PackagePath).Path
$isInsideWorkTree = (& git -C $toolRootPath rev-parse --is-inside-work-tree).Trim()
if ($LASTEXITCODE -ne 0 -or $isInsideWorkTree -ne 'true')
{
    throw 'Could not locate the Git repository for release validation.'
}

$findings = [System.Collections.Generic.List[string]]::new()

function Test-TextForSensitiveData
{
    param(
        [Parameter(Mandatory)]
        [string]$Text,

        [Parameter(Mandatory)]
        [string]$Location,

        [switch]$PatOnly
    )

    $patterns = if ($PatOnly)
    {
        @('(?i)\b[a-z0-9]{75}azdo[a-z0-9]{5}\b')
    }
    else
    {
        @(
            '(?im)^\s*(?:\$env:)?ADO_READ_PAT\s*=',
            '(?i)\b[a-z0-9]{75}azdo[a-z0-9]{5}\b',
            '(?i)https://dev\.azure\.com/(?!(?:example|fabricated|original|replacement|first|second|\{organization\})(?:[^a-z0-9-]|$)|ado-read-test-)[^\s"<>]+',
            '(?i)\b[A-Z0-9._%+-]+@(?!example\.(?:com|invalid|test)\b)[A-Z0-9.-]+\.[A-Z]{2,}\b'
        )
    }

    foreach ($pattern in $patterns)
    {
        if ($Text -match $pattern)
        {
            $findings.Add("Possible credential or live fixture data in $Location.")
            return
        }
    }
}

$trackedFiles = & git -C $toolRootPath ls-files
if ($LASTEXITCODE -ne 0)
{
    throw 'Could not enumerate tracked files for release validation.'
}

foreach ($trackedFile in $trackedFiles)
{
    $fullPath = Join-Path $toolRootPath $trackedFile
    if (Test-Path -LiteralPath $fullPath -PathType Leaf)
    {
        Test-TextForSensitiveData -Text (Get-Content -LiteralPath $fullPath -Raw) -Location $trackedFile
    }
}

$archive = [System.IO.Compression.ZipFile]::OpenRead($packageFullPath)
try
{
    $fixtureEntries = $archive.Entries | Where-Object { $_.FullName -match '(?i)(^|/)(tests?|fixtures?)/' }
    foreach ($entry in $fixtureEntries)
    {
        $findings.Add("Package contains test or fixture content: $($entry.FullName).")
    }

    $noticesEntry = $archive.Entries | Where-Object { $_.FullName -eq 'THIRD-PARTY-NOTICES.md' } | Select-Object -First 1
    if ($null -eq $noticesEntry)
    {
        $findings.Add('Package does not contain THIRD-PARTY-NOTICES.md.')
    }
    else
    {
        $noticesReader = [System.IO.StreamReader]::new($noticesEntry.Open())
        try
        {
            $noticesText = $noticesReader.ReadToEnd()
        }
        finally
        {
            $noticesReader.Dispose()
        }

        if ($noticesText -notmatch 'Git Credential Manager' -or $noticesText -notmatch 'MIT License')
        {
            $findings.Add('THIRD-PARTY-NOTICES.md does not contain the Git Credential Manager MIT License notice.')
        }
    }

    if ($PackageId)
    {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if ($null -eq $nuspecEntry)
        {
            $findings.Add('Package does not contain a nuspec manifest.')
        }
        else
        {
            $nuspecReader = [System.IO.StreamReader]::new($nuspecEntry.Open())
            try
            {
                [xml]$nuspec = $nuspecReader.ReadToEnd()
            }
            finally
            {
                $nuspecReader.Dispose()
            }

            $metadata = $nuspec.package.metadata
            if ($metadata.id -ne $PackageId -or $metadata.version -ne $PackageVersion)
            {
                $findings.Add("Package manifest declares $($metadata.id) $($metadata.version), expected $PackageId $PackageVersion.")
            }
        }
    }

    $singleByteEncoding = [System.Text.Encoding]::GetEncoding(28591)
    foreach ($entry in $archive.Entries)
    {
        if ($entry.Length -eq 0)
        {
            continue
        }

        $entryStream = $entry.Open()
        $buffer = [System.IO.MemoryStream]::new()
        try
        {
            $entryStream.CopyTo($buffer)
            $isBinary = $entry.FullName -match '(?i)\.(dll|exe|pdb)$'
            Test-TextForSensitiveData -Text $singleByteEncoding.GetString($buffer.ToArray()) -Location "package:$($entry.FullName)" -PatOnly:$isBinary
        }
        finally
        {
            $buffer.Dispose()
            $entryStream.Dispose()
        }
    }
}
finally
{
    $archive.Dispose()
}

if ($findings.Count -gt 0)
{
    throw ($findings -join [Environment]::NewLine)
}

Write-Host "Release package validation passed: $packageFullPath"
exit 0
