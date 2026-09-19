[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$benchmark = Join-Path $PSScriptRoot '..\Test-3waSshDrive.ps1'
$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    '3waSshDrive-BenchmarkScriptTest-' + [guid]::NewGuid().ToString('N'))
$targetRoot = Join-Path $testRoot 'target'

try {
    New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null

    $commonArguments = @{
        TargetRoot = $targetRoot
        SmallFileCount = 8
        SmallFileSizesKiB = @(1, 2)
        LargeFileMiB = @(1)
        DirectoryCount = 2
        ProgressInterval = 1
        MetadataPhaseTimeoutSeconds = 60
        MetadataFirst = $true
        RenameItemCount = 2
        MixedOperationCount = 8
        VerificationMiB = 1
    }
    $reportPath = Join-Path $testRoot 'benchmark-report.json'

    $dryRun = @(& $benchmark @commonArguments -ReportPath $reportPath)
    if ($dryRun.Count -ne 1 -or -not $dryRun[0].DryRun) {
        throw 'Benchmark script must return exactly one dry-run result without -Apply.'
    }
    if ((Get-ChildItem -LiteralPath $targetRoot -Force | Measure-Object).Count -ne 0) {
        throw 'Dry-run benchmark must not create files under TargetRoot.'
    }
    if ($dryRun[0].Plan.SmallFileCount -ne 8 -or
        $dryRun[0].Plan.LargeFileMiB -ne 1 -or
        $dryRun[0].Plan.ProgressInterval -ne 1 -or
        $dryRun[0].Plan.MetadataPhaseTimeoutSeconds -ne 60 -or
        $dryRun[0].Plan.MetadataFirst -ne $true) {
        throw 'Dry-run benchmark plan did not preserve supplied parameters.'
    }
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw 'Dry-run benchmark did not write the requested JSON report.'
    }
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
    if ($report.DryRun -ne $true -or $report.Plan.SmallFileCount -ne 8) {
        throw 'Dry-run JSON report did not preserve the benchmark result.'
    }

    $applied = @(& $benchmark @commonArguments -Apply)
    if ($applied.Count -ne 1 -or $applied[0].DryRun) {
        throw 'Applied benchmark must return exactly one non-dry-run result.'
    }
    if ($applied[0].Errors.Count -ne 0) {
        throw "Applied benchmark reported $($applied[0].Errors.Count) error(s)."
    }
    if (Test-Path -LiteralPath $applied[0].RemoteTestDirectory) {
        throw 'Applied benchmark must remove its marked remote test directory by default.'
    }
    if (Test-Path -LiteralPath $applied[0].LocalScratchDirectory) {
        throw 'Applied benchmark must remove its marked local scratch directory by default.'
    }
    if ((Get-ChildItem -LiteralPath $targetRoot -Force | Measure-Object).Count -ne 0) {
        throw 'Applied benchmark left files directly below TargetRoot.'
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host '3waSshDrive benchmark script tests passed.'
