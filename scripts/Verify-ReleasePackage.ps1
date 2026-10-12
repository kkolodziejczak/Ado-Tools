[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$PackagePath,

    [string]$ToolRoot
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

    $redactedText = $Text -replace '(?im)^\s*(?:\$env:)?ADO_READ_PAT\s*=\s*["''](?:your-pat|example-pat|fabricated-test-pat)["'']\s*$', ''
    $patterns = if ($PatOnly)
    {
        @('(?i)\b[a-z0-9]{75}azdo[a-z0-9]{5}\b')
    }
    else
    {
        @(
            '(?im)^\s*(?:\$env:)?ADO_READ_PAT\s*=',
            '(?i)\b[a-z0-9]{75}azdo[a-z0-9]{5}\b',
            '(?i)https://dev\.azure\.com/(?!(?:example|fabricated|original|replacement|first|second|\{organization\})(?:[^a-z0-9-]|$))[^\s"<>]+',
            '(?i)\b[A-Z0-9._%+-]+@(?!example\.(?:com|invalid|test)\b)[A-Z0-9.-]+\.[A-Z]{2,}\b'
        )
    }

    foreach ($pattern in $patterns)
    {
        if ($redactedText -match $pattern)
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
