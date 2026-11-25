# Checks the nine tool packages that gh-release-signing signed, before they go to nuget.org:
#
#   - every package of the release is there, at that version, and nothing else;
#   - every Ado-Tools assembly inside it (AdoRead.*.dll, AdoWrite.*.dll) has a valid Authenticode
#     signature from the expected certificate, with a timestamp;
#   - the package itself has a NuGet author signature from that certificate, with a timestamp.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PackageDirectory,

    [Parameter(Mandatory)]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$ExpectedSignerThumbprint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Security.Cryptography.Pkcs

$packageIds = @(
    'ado-read', 'ado-read-pr', 'ado-read-work-item', 'ado-read-wiki', 'ado-read-sprint',
    'ado-read-pipeline', 'ado-write', 'ado-write-pr', 'ado-write-work-item'
)
$expectedNames = @($packageIds | ForEach-Object { "$_.$PackageVersion.nupkg" } | Sort-Object)
$actualNames = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg' -File | ForEach-Object Name | Sort-Object)
if (Compare-Object $expectedNames $actualNames)
{
    throw "Found packages '$($actualNames -join "', '")'; expected '$($expectedNames -join "', '")'."
}

function Assert-AuthenticodeSigned([string]$Path, [string]$Label)
{
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $signer = $signature.SignerCertificate
    if ($signature.Status -ne 'Valid' -or $null -eq $signer -or $signer.Thumbprint -ne $ExpectedSignerThumbprint)
    {
        $from = if ($signer) { $signer.Subject } else { 'no certificate' }
        throw "'$Label' signature is '$($signature.Status)' from '$from'; expected certificate $ExpectedSignerThumbprint."
    }
    if ($null -eq $signature.TimeStamperCertificate) { throw "'$Label' signature has no timestamp." }
    return $signature.SignerCertificate
}

foreach ($name in $expectedNames)
{
    $package = Join-Path $PackageDirectory $name
    $unpacked = Join-Path ([IO.Path]::GetTempPath()) "ado-tools-signed-$([guid]::NewGuid().ToString('N'))"
    try
    {
        [IO.Compression.ZipFile]::ExtractToDirectory($package, $unpacked)
        $assemblies = @(Get-ChildItem -LiteralPath $unpacked -Recurse -File -Include 'AdoRead.*.dll', 'AdoWrite.*.dll')
        if ($assemblies.Count -eq 0) { throw "'$name' holds no AdoRead.*.dll or AdoWrite.*.dll." }
        foreach ($assembly in $assemblies)
        {
            $certificate = Assert-AuthenticodeSigned $assembly.FullName "$name/$($assembly.FullName.Substring($unpacked.Length + 1))"
        }

        $fingerprint = [BitConverter]::ToString([Security.Cryptography.SHA256]::HashData($certificate.RawData)) -replace '-', ''
        dotnet nuget verify $package --all --certificate-fingerprint $fingerprint
        if ($LASTEXITCODE -ne 0) { throw "dotnet nuget verify failed for '$name' with exit code $LASTEXITCODE." }

        # dotnet nuget verify only warns (NU3027) when the package signature has no timestamp.
        $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
        $cms.Decode([IO.File]::ReadAllBytes((Join-Path $unpacked '.signature.p7s')))
        if (-not ($cms.SignerInfos[0].UnsignedAttributes | Where-Object { $_.Oid.Value -eq '1.2.840.113549.1.9.16.2.14' }))
        {
            throw "'$name' package signature has no timestamp."
        }
        Write-Host "$name and its $($assemblies.Count) Ado-Tools assemblies are signed by $ExpectedSignerThumbprint and timestamped."
    }
    finally
    {
        if (Test-Path -LiteralPath $unpacked) { Remove-Item -LiteralPath $unpacked -Recurse -Force }
    }
}
