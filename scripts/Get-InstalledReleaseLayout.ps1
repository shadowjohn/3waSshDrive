[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$MainExeName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedRoot = (Resolve-Path -LiteralPath $InstallRoot).Path
if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) {
    throw "Install root does not exist: $InstallRoot"
}

$launcherExe = Join-Path $resolvedRoot $MainExeName
$updateExe = Join-Path $resolvedRoot 'Update.exe'
$applicationExe = Join-Path (Join-Path $resolvedRoot 'current') $MainExeName

if (-not (Test-Path -LiteralPath $launcherExe -PathType Leaf)) {
    throw "Velopack execution stub missing: $launcherExe"
}
if (-not (Test-Path -LiteralPath $updateExe -PathType Leaf)) {
    throw "Velopack updater missing: $updateExe"
}
if (-not (Test-Path -LiteralPath $applicationExe -PathType Leaf)) {
    throw "Installed application executable missing: $applicationExe"
}

[pscustomobject]@{
    InstallRoot = $resolvedRoot
    LauncherExe = (Resolve-Path -LiteralPath $launcherExe).Path
    UpdateExe = (Resolve-Path -LiteralPath $updateExe).Path
    ApplicationExe = (Resolve-Path -LiteralPath $applicationExe).Path
}
