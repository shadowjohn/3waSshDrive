[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$InstalledExe,
    [Parameter(Mandatory = $true)][string]$LockPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedInstalledExe = (Resolve-Path -LiteralPath $InstalledExe).Path
if (-not (Test-Path -LiteralPath $resolvedInstalledExe -PathType Leaf)) {
    throw "Installed executable does not exist: $InstalledExe"
}

$resolvedLockPath = [System.IO.Path]::GetFullPath($LockPath)
$lockDirectory = Split-Path -Parent $resolvedLockPath
New-Item -ItemType Directory -Path $lockDirectory -Force | Out-Null
$staleLock = "999999`r`n2000-01-01 00:00:00"

1..2 | ForEach-Object {
    Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
    $process = Start-Process `
        -FilePath $resolvedInstalledExe `
        -ArgumentList '--self-check' `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installed self-check #$_ failed: $($process.ExitCode)"
    }
}

Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
$handle = [System.IO.File]::Open(
    $resolvedLockPath,
    [System.IO.FileMode]::OpenOrCreate,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    $check = Start-Process `
        -FilePath $resolvedInstalledExe `
        -ArgumentList '--self-check' `
        -Wait `
        -PassThru
    if ($check.ExitCode -ne 0) {
        throw 'Self-check was blocked by live lock handle.'
    }
}
finally {
    $handle.Dispose()
}

$finalCheck = Start-Process `
    -FilePath $resolvedInstalledExe `
    -ArgumentList '--self-check' `
    -Wait `
    -PassThru
if ($finalCheck.ExitCode -ne 0) {
    throw "Self-check after stale lock failed: $($finalCheck.ExitCode)"
}

Write-Host 'Installed update restart and stale-lock smoke passed.'
