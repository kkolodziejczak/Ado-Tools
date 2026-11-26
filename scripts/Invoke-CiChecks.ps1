<#
.SYNOPSIS
Runs every check of the CI workflow, in the same order, so a push never meets a failure that a
local run could have shown. .github/workflows/ci.yml calls this script too, so the two cannot drift.

.EXAMPLE
./scripts/Invoke-CiChecks.ps1
./scripts/Invoke-CiChecks.ps1 -Version 1.2.3-preview.4
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$projects = @(
    'src/AdoRead.Core/AdoRead.Core.csproj'
    'src/AdoWrite.Core/AdoWrite.Core.csproj'
    'tests/AdoRead.UnitTests/AdoRead.UnitTests.csproj'
    'tests/AdoRead.IntegrationTests/AdoRead.IntegrationTests.csproj'
    'tests/AdoRead.FakeHost/AdoRead.FakeHost.csproj'
    'tests/AdoWrite.UnitTests/AdoWrite.UnitTests.csproj'
)
$tools = @(
    @{ PackageId = 'ado-read'; Project = 'src/AdoRead.Cli/AdoRead.Cli.csproj' }
    @{ PackageId = 'ado-read-pr'; Project = 'src/AdoRead.PullRequests.Cli/AdoRead.PullRequests.Cli.csproj' }
    @{ PackageId = 'ado-read-work-item'; Project = 'src/AdoRead.WorkItems.Cli/AdoRead.WorkItems.Cli.csproj' }
    @{ PackageId = 'ado-read-wiki'; Project = 'src/AdoRead.Wiki.Cli/AdoRead.Wiki.Cli.csproj' }
    @{ PackageId = 'ado-read-sprint'; Project = 'src/AdoRead.Sprints.Cli/AdoRead.Sprints.Cli.csproj' }
    @{ PackageId = 'ado-read-pipeline'; Project = 'src/AdoRead.Pipelines.Cli/AdoRead.Pipelines.Cli.csproj' }
    @{ PackageId = 'ado-write'; Project = 'src/AdoWrite.Cli/AdoWrite.Cli.csproj' }
    @{ PackageId = 'ado-write-pr'; Project = 'src/AdoWrite.PullRequests.Cli/AdoWrite.PullRequests.Cli.csproj' }
    @{ PackageId = 'ado-write-work-item'; Project = 'src/AdoWrite.WorkItems.Cli/AdoWrite.WorkItems.Cli.csproj' }
)

function Invoke-Step([string] $Name, [scriptblock] $Action) {
    Write-Host "::group::$Name"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$Name failed (exit $LASTEXITCODE)." }
    Write-Host '::endgroup::'
}

Invoke-Step 'Restore locked dependencies' {
    foreach ($project in $projects + $tools.Project) {
        dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) { return }
    }
}

Invoke-Step 'Verify formatting' { dotnet format AdoRead.slnx --verify-no-changes --no-restore }
# Directory.Build.props sets ContinuousIntegrationBuild only on GitHub Actions; set here, a local run
# builds the same deterministic binaries that the package health check expects.
Invoke-Step 'Build release artifacts' { dotnet build AdoRead.slnx --no-restore --configuration Release "-p:Version=$Version" -p:ContinuousIntegrationBuild=true }
Invoke-Step 'Run tests' { dotnet test AdoRead.slnx --no-build --configuration Release }

Invoke-Step 'Pack tools' {
    Remove-Item artifacts/packages -Recurse -Force -ErrorAction SilentlyContinue
    foreach ($tool in $tools) {
        dotnet pack $tool.Project --no-build --configuration Release "-p:Version=$Version"
        if ($LASTEXITCODE -ne 0) { return }
    }
}

function Get-ToolPackage([hashtable] $Tool) {
    $package = Get-ChildItem "artifacts/packages/$($Tool.PackageId).$Version.nupkg" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $package) { throw "Expected $($Tool.PackageId) package was not produced." }
    $package.FullName
}

Invoke-Step 'Validate package contents' {
    foreach ($tool in $tools) {
        ./scripts/Verify-ReleasePackage.ps1 -PackagePath (Get-ToolPackage $tool) -PackageId $tool.PackageId -PackageVersion $Version
    }
}

# NuGet Package Explorer's checks: Source Link, deterministic binaries, compiler flags.
# The tool exits 0 even when a check fails, so the report is read instead.
Invoke-Step 'Check package health' {
    $failed = @()
    $crossMark = [char]0x274C # written as a code so Windows PowerShell 5.1 reads this file the same as pwsh
    foreach ($tool in $tools) {
        $report = dnx dotnet-validate --version 0.0.1-preview.582 --yes -- package local (Get-ToolPackage $tool) | Out-String
        Write-Host $report
        if ($LASTEXITCODE -ne 0 -or $report -match "$crossMark|Non deterministic|Invalid|Missing") { $failed += $tool.PackageId }
    }
    if ($failed.Count -gt 0) { throw "Package health checks failed: $($failed -join ', ')" }
}

Invoke-Step 'Smoke test installed packages' {
    foreach ($tool in $tools) {
        ./scripts/Test-ToolPackage.ps1 -PackagePath (Get-ToolPackage $tool) -PackageId $tool.PackageId -CommandName $tool.PackageId -PackageVersion $Version
    }
}

Write-Host "All CI checks passed for version $Version."
