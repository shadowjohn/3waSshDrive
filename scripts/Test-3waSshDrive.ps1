[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TargetRoot,

    [ValidateRange(1, 100000)]
    [int]$SmallFileCount = 5000,

    [ValidateRange(1, 1024)]
    [int[]]$SmallFileSizesKiB = @(1, 4, 16, 32),

    [ValidateRange(1, 10240)]
    [int[]]$LargeFileMiB = @(100, 1024),

    [ValidateRange(1, 100000)]
    [int]$DirectoryCount = 1000,

    [ValidateRange(1, 100000)]
    [int]$ProgressInterval = 100,

    [ValidateRange(1, 3600)]
    [int]$MetadataPhaseTimeoutSeconds = 300,

    [switch]$MetadataFirst,

    [ValidateRange(1, 100000)]
    [int]$RenameItemCount = 1000,

    [ValidateRange(1, 100000)]
    [int]$MixedOperationCount = 2000,

    [ValidateRange(1, 10240)]
    [int]$VerificationMiB = 64,

    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$')]
    [string]$TestDirectoryPrefix = '_3waSshDrive-benchmark',

    [switch]$Apply,

    [switch]$KeepArtifacts,

    [string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:benchmarkMetrics = [System.Collections.Generic.List[object]]::new()
$script:benchmarkErrors = [System.Collections.Generic.List[object]]::new()

function Resolve-FileSystemDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description does not exist or is not a directory: $Path"
    }

    $resolved = Resolve-Path -LiteralPath $Path
    if ($resolved.Provider.Name -ne 'FileSystem') {
        throw "$Description must use the FileSystem provider: $Path"
    }

    return $resolved.Path
}

function Get-SafeChildPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Parent,

        [Parameter(Mandatory = $true)]
        [string]$ChildName
    )

    $parentWithSeparator = $Parent.TrimEnd([char[]]@('\', '/')) +
        [System.IO.Path]::DirectorySeparatorChar
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Parent $ChildName))
    if (-not $candidate.StartsWith(
            $parentWithSeparator,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Benchmark path escaped its target root: $candidate"
    }

    return $candidate
}

function New-PatternBytes {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Length,

        [Parameter(Mandatory = $true)]
        [int]$Seed
    )

    $bytes = [byte[]]::new($Length)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        $bytes[$index] = [byte]((($index + $Seed) * 31) % 251)
    }
    return $bytes
}

function New-PatternFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [long]$LengthBytes,

        [Parameter(Mandatory = $true)]
        [int]$Seed
    )

    $bufferSize = [Math]::Min(1MB, [Math]::Max(1, [int]$LengthBytes))
    $buffer = [byte[]]::new($bufferSize)
    $stream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        $bufferSize,
        [System.IO.FileOptions]::SequentialScan)

    try {
        [long]$written = 0
        while ($written -lt $LengthBytes) {
            $chunkLength = [Math]::Min($buffer.Length, [int]($LengthBytes - $written))
            for ($index = 0; $index -lt $chunkLength; $index++) {
                $buffer[$index] = [byte]((($written + $index + $Seed) * 31) % 251)
            }
            $stream.Write($buffer, 0, $chunkLength)
            $written += $chunkLength
        }
        $stream.Flush()
    }
    finally {
        $stream.Dispose()
    }
}

function Copy-SequentialFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    $bufferSize = 1MB
    $buffer = [byte[]]::new($bufferSize)
    $sourceStream = [System.IO.FileStream]::new(
        $Source,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read,
        $bufferSize,
        [System.IO.FileOptions]::SequentialScan)
    $destinationStream = [System.IO.FileStream]::new(
        $Destination,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        $bufferSize,
        [System.IO.FileOptions]::SequentialScan)
    $timer = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        [long]$totalBytes = 0
        while (($read = $sourceStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $destinationStream.Write($buffer, 0, $read)
            $totalBytes += $read
        }
        $destinationStream.Flush()
        $timer.Stop()
        return [pscustomobject]@{
            Bytes = $totalBytes
            Seconds = $timer.Elapsed.TotalSeconds
        }
    }
    finally {
        if ($timer.IsRunning) {
            $timer.Stop()
        }
        $destinationStream.Dispose()
        $sourceStream.Dispose()
    }
}

function Invoke-BenchmarkPhase {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    Write-Host "Running              : $Name"
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $result = & $Action
        $timer.Stop()
        return [pscustomobject]@{
            Name = $Name
            Succeeded = $true
            Result = $result
            Seconds = $timer.Elapsed.TotalSeconds
            ErrorMessage = $null
        }
    }
    catch {
        $timer.Stop()
        $message = $_.Exception.Message
        $script:benchmarkErrors.Add([pscustomobject]@{
                Phase = $Name
                Message = $message
            })
        Write-Warning "$Name failed: $message"
        return [pscustomobject]@{
            Name = $Name
            Succeeded = $false
            Result = $null
            Seconds = $timer.Elapsed.TotalSeconds
            ErrorMessage = $message
        }
    }
}

function Add-BenchmarkMetric {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [string]$Unit,

        [long]$Count = 0,

        [long]$Bytes = 0,

        [double]$Seconds = 0,

        [bool]$Passed = $true,

        [string]$ErrorMessage
    )

    $value = if (-not $Passed) {
        'FAIL'
    }
    elseif ($Unit -eq 'files/sec' -or $Unit -eq 'ops/sec') {
        if ($Seconds -gt 0) {
            ('{0:N1} {1}' -f ($Count / $Seconds), $Unit)
        }
        else {
            "n/a $Unit"
        }
    }
    elseif ($Unit -eq 'MB/s') {
        if ($Seconds -gt 0) {
            ('{0:N1} MB/s' -f (($Bytes / 1MB) / $Seconds))
        }
        else {
            'n/a MB/s'
        }
    }
    elseif ($Unit -eq 'result') {
        'PASS'
    }
    else {
        'n/a'
    }

    $script:benchmarkMetrics.Add([pscustomobject]@{
            Label = $Label
            Unit = $Unit
            Count = $Count
            Bytes = $Bytes
            Seconds = [Math]::Round($Seconds, 3)
            Value = $value
            Passed = $Passed
            ErrorMessage = $ErrorMessage
    })
}

function Write-BenchmarkProgress {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Phase,

        [Parameter(Mandatory = $true)]
        [int]$Completed,

        [Parameter(Mandatory = $true)]
        [int]$Total,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$Timer
    )

    if ($Completed -eq $Total -or ($Completed % $ProgressInterval) -eq 0) {
        Write-Host ('Progress             : {0} {1}/{2} ({3:N1}s)' -f `
                $Phase, $Completed, $Total, $Timer.Elapsed.TotalSeconds)
    }
}

function Assert-MetadataPhaseDeadline {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Phase,

        [Parameter(Mandatory = $true)]
        [int]$Completed,

        [Parameter(Mandatory = $true)]
        [int]$Total,

        [Parameter(Mandatory = $true)]
        [System.Diagnostics.Stopwatch]$Timer
    )

    if ($Timer.Elapsed.TotalSeconds -gt $MetadataPhaseTimeoutSeconds) {
        throw "$Phase exceeded the $MetadataPhaseTimeoutSeconds second metadata limit after $Completed/$Total operations."
    }
}

function Invoke-DirectoryMetadataBenchmark {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory
    )

    $metadataDirectory = Join-Path $RunDirectory 'metadata'
    $directoryPaths = [System.Collections.Generic.List[string]]::new()
    $directoryCreate = Invoke-BenchmarkPhase -Name 'Directories create' -Action {
        New-Item -ItemType Directory -Path $metadataDirectory | Out-Null
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        for ($index = 0; $index -lt $DirectoryCount; $index++) {
            $path = Join-Path $metadataDirectory ('dir-{0:D5}' -f $index)
            New-Item -ItemType Directory -Path $path | Out-Null
            $directoryPaths.Add($path)
            Write-BenchmarkProgress -Phase 'Directories create' -Completed ($index + 1) -Total $DirectoryCount -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Directories create' -Completed ($index + 1) -Total $DirectoryCount -Timer $phaseTimer
        }
    }
    Add-BenchmarkMetric -Label 'Directories create' -Unit 'ops/sec' -Count $DirectoryCount -Seconds $directoryCreate.Seconds -Passed $directoryCreate.Succeeded -ErrorMessage $directoryCreate.ErrorMessage

    $directoryStat = Invoke-BenchmarkPhase -Name 'Directories stat' -Action {
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        for ($index = 0; $index -lt $directoryPaths.Count; $index++) {
            $path = $directoryPaths[$index]
            Get-Item -LiteralPath $path | Out-Null
            Write-BenchmarkProgress -Phase 'Directories stat' -Completed ($index + 1) -Total $directoryPaths.Count -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Directories stat' -Completed ($index + 1) -Total $directoryPaths.Count -Timer $phaseTimer
        }
    }
    Add-BenchmarkMetric -Label 'Directories stat' -Unit 'ops/sec' -Count $directoryPaths.Count -Seconds $directoryStat.Seconds -Passed $directoryStat.Succeeded -ErrorMessage $directoryStat.ErrorMessage

    return [pscustomobject]@{
        MetadataDirectory = $metadataDirectory
        DirectoryPaths = $directoryPaths
    }
}

function Test-BenchmarkMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId
    )

    $markerPath = Join-Path $RunDirectory '.3waSshDrive-benchmark-marker.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        return $false
    }

    try {
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        return $marker.Tool -eq '3waSshDrive Benchmark' -and $marker.RunId -eq $RunId
    }
    catch {
        return $false
    }
}

function Write-BenchmarkMarker {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [Parameter(Mandatory = $true)]
        [datetime]$StartedAt
    )

    $marker = [pscustomobject]@{
        Tool = '3waSshDrive Benchmark'
        SchemaVersion = 1
        RunId = $RunId
        StartedAt = $StartedAt.ToString('o')
    }
    $markerPath = Join-Path $RunDirectory '.3waSshDrive-benchmark-marker.json'
    [System.IO.File]::WriteAllText(
        $markerPath,
        ($marker | ConvertTo-Json),
        [System.Text.UTF8Encoding]::new($false))
}

function Remove-BenchmarkDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunId,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-BenchmarkMarker -RunDirectory $RunDirectory -RunId $RunId)) {
        throw "Refusing to remove $Description because its benchmark marker is missing or invalid: $RunDirectory"
    }

    Remove-Item -LiteralPath $RunDirectory -Recurse -Force
}

function Write-BenchmarkReport {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    if ([string]::IsNullOrWhiteSpace($ReportPath)) {
        return
    }

    $fullReportPath = [System.IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = [System.IO.Path]::GetDirectoryName($fullReportPath)
    if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $fullReportPath,
        ($Result | ConvertTo-Json -Depth 7),
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "JSON report          : $fullReportPath"
}

function Write-BenchmarkSummary {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )

    Write-Host ''
    Write-Host '3waSshDrive Benchmark result'
    Write-Host "Target root          : $($Result.TargetRoot)"
    foreach ($metric in $Result.Metrics) {
        Write-Host ('{0,-22} : {1}' -f $metric.Label, $metric.Value)
    }
    Write-Host ('{0,-22} : {1}' -f 'Errors', $Result.Errors.Count)
    if ($Result.Errors.Count -gt 0) {
        foreach ($benchmarkError in $Result.Errors) {
            Write-Host "  [$($benchmarkError.Phase)] $($benchmarkError.Message)"
        }
    }
}

$resolvedTargetRoot = Resolve-FileSystemDirectory -Path $TargetRoot -Description 'TargetRoot'
$runId = '{0:yyyyMMdd-HHmmss}-{1}' -f (Get-Date), [guid]::NewGuid().ToString('N')
$runDirectoryName = "$TestDirectoryPrefix-$runId"
$runDirectory = Get-SafeChildPath -Parent $resolvedTargetRoot -ChildName $runDirectoryName
$localScratchDirectory = Join-Path ([System.IO.Path]::GetTempPath()) $runDirectoryName

[long]$smallFileBytes = 0
for ($index = 0; $index -lt $SmallFileCount; $index++) {
    $smallFileBytes += [long]$SmallFileSizesKiB[$index % $SmallFileSizesKiB.Count] * 1KB
}
[long]$largeFileBytes = 0
foreach ($sizeMiB in $LargeFileMiB) {
    $largeFileBytes += [long]$sizeMiB * 1MB
}
[long]$remoteEstimateBytes = $smallFileBytes + $largeFileBytes + ([long]$VerificationMiB * 1MB)
[long]$localEstimateBytes = ($largeFileBytes * 2) + ([long]$VerificationMiB * 2 * 1MB)

$plan = [pscustomobject]@{
    SmallFileCount = $SmallFileCount
    SmallFileSizesKiB = @($SmallFileSizesKiB)
    LargeFileMiB = @($LargeFileMiB)
    DirectoryCount = $DirectoryCount
    ProgressInterval = $ProgressInterval
    MetadataPhaseTimeoutSeconds = $MetadataPhaseTimeoutSeconds
    MetadataFirst = $MetadataFirst.IsPresent
    RenameItemCount = $RenameItemCount
    MixedOperationCount = $MixedOperationCount
    VerificationMiB = $VerificationMiB
    EstimatedRemoteBytes = $remoteEstimateBytes
    EstimatedLocalScratchBytes = $localEstimateBytes
}

if (-not $Apply) {
    $dryRunResult = [pscustomobject]@{
        Tool = '3waSshDrive Benchmark'
        DryRun = $true
        StartedAt = (Get-Date).ToString('o')
        TargetRoot = $resolvedTargetRoot
        RemoteTestDirectory = $runDirectory
        LocalScratchDirectory = $localScratchDirectory
        Plan = $plan
        Metrics = @()
        Errors = @()
        KeptArtifacts = $false
    }
    Write-Host 'Dry run only. No files were created or changed.'
    Write-Host "Target root          : $resolvedTargetRoot"
    Write-Host "Remote test directory: $runDirectory"
    Write-Host "Small files          : $SmallFileCount (1 KiB to 32 KiB by default)"
    Write-Host "Sequential files     : $($LargeFileMiB -join ', ') MiB"
    Write-Host "Estimated remote use : $([Math]::Round($remoteEstimateBytes / 1GB, 2)) GiB"
    Write-Host "Estimated local use  : $([Math]::Round($localEstimateBytes / 1GB, 2)) GiB"
    Write-Host 'Run again with -Apply only against a writable, disposable mounted directory.'
    Write-BenchmarkReport -Result $dryRunResult
    return $dryRunResult
}

$startedAt = Get-Date
$remoteCreated = $false
$localCreated = $false
$preserveArtifacts = $KeepArtifacts.IsPresent

try {
    # TargetRoot may already be a large working directory. Only stat it here;
    # enumeration is intentionally measured inside this run's empty test tree.
    Write-Host 'Preflight            : target directory stat'
    Get-Item -LiteralPath $resolvedTargetRoot -Force -ErrorAction Stop | Out-Null

    New-Item -ItemType Directory -Path $runDirectory -ErrorAction Stop | Out-Null
    $remoteCreated = $true
    Write-BenchmarkMarker -RunDirectory $runDirectory -RunId $runId -StartedAt $startedAt

    New-Item -ItemType Directory -Path $localScratchDirectory -ErrorAction Stop | Out-Null
    $localCreated = $true
    Write-BenchmarkMarker -RunDirectory $localScratchDirectory -RunId $runId -StartedAt $startedAt

    $metadataBenchmark = $null
    if ($MetadataFirst) {
        $metadataBenchmark = Invoke-DirectoryMetadataBenchmark -RunDirectory $runDirectory
    }

    $smallDirectory = Join-Path $runDirectory 'small-files'
    $smallPayloads = @{}
    foreach ($sizeKiB in $SmallFileSizesKiB) {
        $smallPayloads[$sizeKiB] = New-PatternBytes -Length ($sizeKiB * 1KB) -Seed $sizeKiB
    }

    $smallCreate = Invoke-BenchmarkPhase -Name 'Small files create' -Action {
        New-Item -ItemType Directory -Path $smallDirectory | Out-Null
        [long]$bytes = 0
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        for ($index = 0; $index -lt $SmallFileCount; $index++) {
            $sizeKiB = $SmallFileSizesKiB[$index % $SmallFileSizesKiB.Count]
            $payload = $smallPayloads[$sizeKiB]
            $path = Join-Path $smallDirectory ('file-{0:D6}.bin' -f $index)
            [System.IO.File]::WriteAllBytes($path, $payload)
            $bytes += $payload.Length
            Write-BenchmarkProgress -Phase 'Small files create' -Completed ($index + 1) -Total $SmallFileCount -Timer $phaseTimer
        }
        return [pscustomobject]@{ Count = $SmallFileCount; Bytes = $bytes }
    }
    Add-BenchmarkMetric -Label 'Small files create' -Unit 'files/sec' -Count $SmallFileCount -Bytes $smallFileBytes -Seconds $smallCreate.Seconds -Passed $smallCreate.Succeeded -ErrorMessage $smallCreate.ErrorMessage

    $smallRead = Invoke-BenchmarkPhase -Name 'Small files read' -Action {
        [long]$bytes = 0
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        for ($index = 0; $index -lt $SmallFileCount; $index++) {
            $path = Join-Path $smallDirectory ('file-{0:D6}.bin' -f $index)
            $bytes += [System.IO.File]::ReadAllBytes($path).Length
            Write-BenchmarkProgress -Phase 'Small files read' -Completed ($index + 1) -Total $SmallFileCount -Timer $phaseTimer
        }
        return [pscustomobject]@{ Count = $SmallFileCount; Bytes = $bytes }
    }
    Add-BenchmarkMetric -Label 'Small files read' -Unit 'files/sec' -Count $SmallFileCount -Bytes $smallFileBytes -Seconds $smallRead.Seconds -Passed $smallRead.Succeeded -ErrorMessage $smallRead.ErrorMessage

    foreach ($sizeMiB in $LargeFileMiB) {
        [long]$sizeBytes = [long]$sizeMiB * 1MB
        $sourcePath = Join-Path $localScratchDirectory "source-$sizeMiB-mib.bin"
        $remotePath = Join-Path $runDirectory "sequential-$sizeMiB-mib.bin"
        $returnedPath = Join-Path $localScratchDirectory "returned-$sizeMiB-mib.bin"

        $sourcePhase = Invoke-BenchmarkPhase -Name "Prepare $sizeMiB MiB source" -Action {
            New-PatternFile -Path $sourcePath -LengthBytes $sizeBytes -Seed $sizeMiB
        }
        if (-not $sourcePhase.Succeeded) {
            Add-BenchmarkMetric -Label "$sizeMiB MiB sequential write" -Unit 'MB/s' -Bytes $sizeBytes -Seconds $sourcePhase.Seconds -Passed $false -ErrorMessage $sourcePhase.ErrorMessage
            Add-BenchmarkMetric -Label "$sizeMiB MiB sequential read" -Unit 'MB/s' -Bytes $sizeBytes -Seconds $sourcePhase.Seconds -Passed $false -ErrorMessage $sourcePhase.ErrorMessage
            continue
        }

        $writePhase = Invoke-BenchmarkPhase -Name "$sizeMiB MiB sequential write" -Action {
            Copy-SequentialFile -Source $sourcePath -Destination $remotePath
        }
        Add-BenchmarkMetric -Label "$sizeMiB MiB sequential write" -Unit 'MB/s' -Bytes $sizeBytes -Seconds $writePhase.Seconds -Passed $writePhase.Succeeded -ErrorMessage $writePhase.ErrorMessage

        if ($writePhase.Succeeded) {
            $readPhase = Invoke-BenchmarkPhase -Name "$sizeMiB MiB sequential read" -Action {
                Copy-SequentialFile -Source $remotePath -Destination $returnedPath
            }
            Add-BenchmarkMetric -Label "$sizeMiB MiB sequential read" -Unit 'MB/s' -Bytes $sizeBytes -Seconds $readPhase.Seconds -Passed $readPhase.Succeeded -ErrorMessage $readPhase.ErrorMessage
        }
        else {
            Add-BenchmarkMetric -Label "$sizeMiB MiB sequential read" -Unit 'MB/s' -Bytes $sizeBytes -Passed $false -ErrorMessage 'Skipped because sequential write failed.'
        }
    }

    if ($null -eq $metadataBenchmark) {
        $metadataBenchmark = Invoke-DirectoryMetadataBenchmark -RunDirectory $runDirectory
    }
    $metadataDirectory = $metadataBenchmark.MetadataDirectory
    $directoryPaths = $metadataBenchmark.DirectoryPaths

    $renameDirectory = Join-Path $metadataDirectory 'rename-files'
    $renamePaths = [System.Collections.Generic.List[string]]::new()
    $renameSeed = Invoke-BenchmarkPhase -Name 'Rename seed create' -Action {
        New-Item -ItemType Directory -Path $renameDirectory | Out-Null
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        for ($index = 0; $index -lt $RenameItemCount; $index++) {
            $path = Join-Path $renameDirectory ('before-{0:D5}.tmp' -f $index)
            [System.IO.File]::WriteAllBytes($path, [byte[]]@(0))
            $renamePaths.Add($path)
            Write-BenchmarkProgress -Phase 'Rename seed create' -Completed ($index + 1) -Total $RenameItemCount -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Rename seed create' -Completed ($index + 1) -Total $RenameItemCount -Timer $phaseTimer
        }
    }
    Add-BenchmarkMetric -Label 'Rename seed create' -Unit 'ops/sec' -Count $RenameItemCount -Seconds $renameSeed.Seconds -Passed $renameSeed.Succeeded -ErrorMessage $renameSeed.ErrorMessage

    if ($renameSeed.Succeeded) {
        $renamePhase = Invoke-BenchmarkPhase -Name 'Rename files' -Action {
            $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
            for ($index = 0; $index -lt $renamePaths.Count; $index++) {
                $destination = Join-Path $renameDirectory ('after-{0:D5}.tmp' -f $index)
                Move-Item -LiteralPath $renamePaths[$index] -Destination $destination
                $renamePaths[$index] = $destination
                Write-BenchmarkProgress -Phase 'Rename files' -Completed ($index + 1) -Total $renamePaths.Count -Timer $phaseTimer
                Assert-MetadataPhaseDeadline -Phase 'Rename files' -Completed ($index + 1) -Total $renamePaths.Count -Timer $phaseTimer
            }
        }
        Add-BenchmarkMetric -Label "Rename $RenameItemCount files" -Unit 'ops/sec' -Count $RenameItemCount -Seconds $renamePhase.Seconds -Passed $renamePhase.Succeeded -ErrorMessage $renamePhase.ErrorMessage
    }
    else {
        Add-BenchmarkMetric -Label "Rename $RenameItemCount files" -Unit 'ops/sec' -Passed $false -ErrorMessage 'Skipped because rename seed creation failed.'
    }

    $metadataDelete = Invoke-BenchmarkPhase -Name 'Metadata delete' -Action {
        $deleteCount = $renamePaths.Count + $directoryPaths.Count
        $completed = 0
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        foreach ($path in $renamePaths) {
            Remove-Item -LiteralPath $path -Force
            $completed++
            Write-BenchmarkProgress -Phase 'Metadata delete' -Completed $completed -Total $deleteCount -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Metadata delete' -Completed $completed -Total $deleteCount -Timer $phaseTimer
        }
        foreach ($path in $directoryPaths) {
            Remove-Item -LiteralPath $path -Force
            $completed++
            Write-BenchmarkProgress -Phase 'Metadata delete' -Completed $completed -Total $deleteCount -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Metadata delete' -Completed $completed -Total $deleteCount -Timer $phaseTimer
        }
    }
    Add-BenchmarkMetric -Label 'Metadata delete' -Unit 'ops/sec' -Count ($renamePaths.Count + $directoryPaths.Count) -Seconds $metadataDelete.Seconds -Passed $metadataDelete.Succeeded -ErrorMessage $metadataDelete.ErrorMessage

    $mixedDirectory = Join-Path $runDirectory 'mixed'
    $mixedPaths = [System.Collections.Generic.List[string]]::new()
    $mixedPhase = Invoke-BenchmarkPhase -Name 'Mixed file operations' -Action {
        New-Item -ItemType Directory -Path $mixedDirectory | Out-Null
        $random = [System.Random]::new(314159)
        $payload = New-PatternBytes -Length 4KB -Seed 77
        $phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
        $seedCount = [Math]::Min(100, [Math]::Max(10, [int]($MixedOperationCount / 10)))
        for ($index = 0; $index -lt $seedCount; $index++) {
            $path = Join-Path $mixedDirectory ('seed-{0:D5}.bin' -f $index)
            [System.IO.File]::WriteAllBytes($path, $payload)
            $mixedPaths.Add($path)
        }

        for ($index = 0; $index -lt $MixedOperationCount; $index++) {
            $operation = $random.Next(100)
            if ($operation -lt 35 -or $mixedPaths.Count -eq 0) {
                $path = Join-Path $mixedDirectory ('write-{0:D6}.bin' -f $index)
                [System.IO.File]::WriteAllBytes($path, $payload)
                $mixedPaths.Add($path)
            }
            elseif ($operation -lt 60) {
                $path = $mixedPaths[$random.Next($mixedPaths.Count)]
                [System.IO.File]::ReadAllBytes($path) | Out-Null
            }
            elseif ($operation -lt 82) {
                $pathIndex = $random.Next($mixedPaths.Count)
                $destination = Join-Path $mixedDirectory ('rename-{0:D6}.bin' -f $index)
                Move-Item -LiteralPath $mixedPaths[$pathIndex] -Destination $destination
                $mixedPaths[$pathIndex] = $destination
            }
            else {
                $pathIndex = $random.Next($mixedPaths.Count)
                Remove-Item -LiteralPath $mixedPaths[$pathIndex] -Force
                $mixedPaths.RemoveAt($pathIndex)
            }
            Write-BenchmarkProgress -Phase 'Mixed file operations' -Completed ($index + 1) -Total $MixedOperationCount -Timer $phaseTimer
            Assert-MetadataPhaseDeadline -Phase 'Mixed file operations' -Completed ($index + 1) -Total $MixedOperationCount -Timer $phaseTimer
        }
    }
    Add-BenchmarkMetric -Label 'Mixed file operations' -Unit 'ops/sec' -Count $MixedOperationCount -Seconds $mixedPhase.Seconds -Passed $mixedPhase.Succeeded -ErrorMessage $mixedPhase.ErrorMessage

    $verificationPhase = Invoke-BenchmarkPhase -Name 'SHA256 verify' -Action {
        [long]$verificationBytes = [long]$VerificationMiB * 1MB
        $verificationSource = Join-Path $localScratchDirectory 'verify-source.bin'
        $verificationRemote = Join-Path $runDirectory 'verify-remote.bin'
        $verificationReturned = Join-Path $localScratchDirectory 'verify-returned.bin'
        New-PatternFile -Path $verificationSource -LengthBytes $verificationBytes -Seed 991
        Copy-SequentialFile -Source $verificationSource -Destination $verificationRemote | Out-Null
        Copy-SequentialFile -Source $verificationRemote -Destination $verificationReturned | Out-Null

        $sourceHash = (Get-FileHash -LiteralPath $verificationSource -Algorithm SHA256).Hash
        $remoteHash = (Get-FileHash -LiteralPath $verificationRemote -Algorithm SHA256).Hash
        $returnedHash = (Get-FileHash -LiteralPath $verificationReturned -Algorithm SHA256).Hash
        if ($sourceHash -ne $remoteHash -or $sourceHash -ne $returnedHash) {
            throw 'SHA256 mismatch between local source, mounted drive, and returned copy.'
        }
    }
    Add-BenchmarkMetric -Label 'SHA256 verify' -Unit 'result' -Seconds $verificationPhase.Seconds -Passed $verificationPhase.Succeeded -ErrorMessage $verificationPhase.ErrorMessage

    # 保留失敗現場，避免後續的 throw 只留下無法重現的數字。
    if ($script:benchmarkErrors.Count -gt 0) {
        $preserveArtifacts = $true
    }
}
catch {
    $preserveArtifacts = $true
    $script:benchmarkErrors.Add([pscustomobject]@{
            Phase = 'Setup'
            Message = $_.Exception.Message
        })
    Write-Warning "Benchmark setup failed: $($_.Exception.Message)"
}
finally {
    if (-not $preserveArtifacts) {
        if ($remoteCreated -and (Test-Path -LiteralPath $runDirectory)) {
            try {
                Remove-BenchmarkDirectory -RunDirectory $runDirectory -RunId $runId -Description 'remote benchmark directory'
            }
            catch {
                $preserveArtifacts = $true
                $script:benchmarkErrors.Add([pscustomobject]@{
                        Phase = 'Remote cleanup'
                        Message = $_.Exception.Message
                    })
                Write-Warning "Remote benchmark cleanup failed: $($_.Exception.Message)"
            }
        }
        if (-not $preserveArtifacts -and $localCreated -and (Test-Path -LiteralPath $localScratchDirectory)) {
            try {
                Remove-BenchmarkDirectory -RunDirectory $localScratchDirectory -RunId $runId -Description 'local benchmark directory'
            }
            catch {
                $preserveArtifacts = $true
                $script:benchmarkErrors.Add([pscustomobject]@{
                        Phase = 'Local cleanup'
                        Message = $_.Exception.Message
                    })
                Write-Warning "Local benchmark cleanup failed: $($_.Exception.Message)"
            }
        }
    }
}

$benchmarkResult = [pscustomobject]@{
    Tool = '3waSshDrive Benchmark'
    DryRun = $false
    StartedAt = $startedAt.ToString('o')
    CompletedAt = (Get-Date).ToString('o')
    TargetRoot = $resolvedTargetRoot
    RemoteTestDirectory = $runDirectory
    LocalScratchDirectory = $localScratchDirectory
    Plan = $plan
    Metrics = @($script:benchmarkMetrics)
    Errors = @($script:benchmarkErrors)
    KeptArtifacts = $preserveArtifacts
}

Write-BenchmarkSummary -Result $benchmarkResult
Write-BenchmarkReport -Result $benchmarkResult

if ($benchmarkResult.Errors.Count -gt 0) {
    throw "Benchmark completed with $($benchmarkResult.Errors.Count) error(s). Artifacts were kept at '$runDirectory' and '$localScratchDirectory' when available."
}

return $benchmarkResult
