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

function Invoke-InstalledSelfCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Label
    )

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        $process = Start-Process `
            -FilePath $resolvedInstalledExe `
            -ArgumentList '--self-check' `
            -Wait `
            -PassThru `
            -RedirectStandardOutput $stdoutPath `
            -RedirectStandardError $stderrPath
        $stdout = [string](Get-Content -LiteralPath $stdoutPath -Raw)
        $stderr = [string](Get-Content -LiteralPath $stderrPath -Raw)
        $diagnostics = ($stdout + "`n" + $stderr).Trim()

        if ($process.ExitCode -ne 0) {
            throw "$Label failed: $($process.ExitCode). $diagnostics"
        }
        if ($diagnostics -notmatch '(?m)^SELF-CHECK OK\b') {
            throw "$Label did not report a successful self-check. $diagnostics"
        }
        if ($diagnostics -notmatch '(?m)\bUpdateMode=Installed\b') {
            throw "$Label did not report UpdateMode=Installed. $diagnostics"
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-NormalLaunchLock {
    param(
        [Parameter(Mandatory = $true)][int]$Attempt
    )

    Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
    $process = Start-Process -FilePath $resolvedInstalledExe -PassThru
    try {
        $deadline = [datetime]::UtcNow.AddSeconds(15)
        $lockAcquired = $false
        while ([datetime]::UtcNow -lt $deadline) {
            if ($process.HasExited) {
                throw (
                    "Normal launch #$Attempt exited before acquiring the " +
                    "instance lock: $($process.ExitCode)")
            }

            try {
                $probe = [System.IO.File]::Open(
                    $resolvedLockPath,
                    [System.IO.FileMode]::OpenOrCreate,
                    [System.IO.FileAccess]::ReadWrite,
                    [System.IO.FileShare]::None)
                $probe.Dispose()
            }
            catch [System.IO.IOException] {
                $lockAcquired = $true
                break
            }

            Start-Sleep -Milliseconds 100
        }

        if (-not $lockAcquired) {
            throw 'Normal launch did not acquire the instance lock.'
        }
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit()
        }
    }
}

1..2 | ForEach-Object {
    Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
    Invoke-InstalledSelfCheck -Label "Installed self-check #$_"
}

Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
$handle = [System.IO.File]::Open(
    $resolvedLockPath,
    [System.IO.FileMode]::OpenOrCreate,
    [System.IO.FileAccess]::ReadWrite,
    [System.IO.FileShare]::None)
try {
    try {
        Invoke-InstalledSelfCheck -Label 'Self-check with live lock handle'
    }
    catch {
        throw "Self-check was blocked by live lock handle. $($_.Exception.Message)"
    }
}
finally {
    $handle.Dispose()
}

Set-Content -LiteralPath $resolvedLockPath -Value $staleLock
Invoke-InstalledSelfCheck -Label 'Self-check after stale lock'

1..2 | ForEach-Object {
    Test-NormalLaunchLock -Attempt $_
}

Write-Host 'Installed update restart and stale-lock smoke passed.'
