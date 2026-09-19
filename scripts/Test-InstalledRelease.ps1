[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SetupPath,
    [Parameter(Mandatory = $true)][string]$ExpectedDisplayVersion,
    [Parameter(Mandatory = $true)][string]$ExpectedPackageVersion,
    [string]$AdditionalSmokeScript
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedSetupPath = (Resolve-Path -LiteralPath $SetupPath).Path
if (-not (Test-Path -LiteralPath $resolvedSetupPath -PathType Leaf)) {
    throw "Setup executable does not exist: $SetupPath"
}

$resolvedAdditionalSmokeScript = $null
if (-not [string]::IsNullOrWhiteSpace($AdditionalSmokeScript)) {
    $resolvedAdditionalSmokeScript = (
        Resolve-Path -LiteralPath $AdditionalSmokeScript
    ).Path
    if (-not (Test-Path -LiteralPath $resolvedAdditionalSmokeScript -PathType Leaf)) {
        throw "Additional smoke script does not exist: $AdditionalSmokeScript"
    }
}

$resolver = Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1'
$expectedVersion = (& $resolver -Tag $ExpectedDisplayVersion | ConvertFrom-Json)
if ($expectedVersion.PackageVersion -ne $ExpectedPackageVersion) {
    throw (
        "Expected package version '$ExpectedPackageVersion' does not match " +
        "display version '$ExpectedDisplayVersion' ($($expectedVersion.PackageVersion)).")
}

$root = Join-Path $env:LOCALAPPDATA '3waSshDrive'
if (Test-Path -LiteralPath $root) {
    throw "Install root already exists: $root"
}

try {
    $setup = Start-Process `
        -FilePath $resolvedSetupPath `
        -ArgumentList '--silent' `
        -Wait `
        -PassThru
    if ($setup.ExitCode -ne 0) {
        throw "Setup failed: $($setup.ExitCode)"
    }

    $layout = & (Join-Path $PSScriptRoot 'Get-InstalledReleaseLayout.ps1') `
        -InstallRoot $root `
        -MainExeName '3waSshDrive.exe'
    $exe = $layout.ApplicationExe

    $check = Start-Process `
        -FilePath $exe `
        -ArgumentList '--self-check' `
        -Wait `
        -PassThru
    if ($check.ExitCode -ne 0) {
        throw "Installed self-check failed: $($check.ExitCode)"
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($versionInfo.ProductVersion -ne $ExpectedDisplayVersion) {
        throw (
            "Installed ProductVersion mismatch. Expected " +
            "'$ExpectedDisplayVersion', got '$($versionInfo.ProductVersion)'.")
    }
    if ($versionInfo.FileVersion -ne $expectedVersion.AssemblyVersion) {
        throw (
            "Installed FileVersion mismatch. Expected " +
            "'$($expectedVersion.AssemblyVersion)', got '$($versionInfo.FileVersion)'.")
    }

    if ($null -ne $resolvedAdditionalSmokeScript) {
        & $resolvedAdditionalSmokeScript `
            -InstalledExe $exe `
            -LockPath (Join-Path $root 'state\lock.pid')
    }

    Write-Host (
        "Installed release validated: " +
        "$ExpectedDisplayVersion ($ExpectedPackageVersion)")
}
finally {
    $updateExe = Join-Path $root 'Update.exe'
    if (Test-Path -LiteralPath $updateExe -PathType Leaf) {
        $uninstall = Start-Process `
            -FilePath $updateExe `
            -ArgumentList '--silent uninstall' `
            -Wait `
            -PassThru
        if ($uninstall.ExitCode -ne 0) {
            throw "Uninstall failed: $($uninstall.ExitCode)"
        }
    }

    $deadline = [datetime]::UtcNow.AddSeconds(30)
    while ((Test-Path -LiteralPath $root) -and [datetime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 250
    }
    if (Test-Path -LiteralPath $root) {
        throw "Uninstall left install root: $root"
    }
}
