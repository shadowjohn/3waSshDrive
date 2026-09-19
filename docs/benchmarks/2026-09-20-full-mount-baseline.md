# 2026-09-20 full mounted-drive baseline

This is the first successful full real-host baseline after adding the shared
directory-listing cache. It was run against the writable disposable mounted
target `T:\tmp\3wasshdrive\_benchmark\_Test`.

```powershell
pwsh -NoProfile -File .\scripts\Test-3waSshDrive.ps1 `
  -TargetRoot 'T:\tmp\3wasshdrive\_benchmark\_Test' `
  -Apply -MetadataFirst `
  -ProgressInterval 100 -MetadataPhaseTimeoutSeconds 300 `
  -ReportPath 'C:\Temp\3waSshDrive-benchmark-full.json'
```

The run used the script defaults: 5,000 small files, 100 MiB and 1 GiB
sequential transfers, 1,000 directory and rename operations, 2,000 mixed
operations, and a 64 MiB SHA-256 verification.

| Metric | Result |
| --- | --- |
| Directories create | 107.2 ops/sec |
| Directories stat | 54.5 ops/sec |
| Small files create | 45.9 files/sec |
| Small files read | 91.1 files/sec |
| 100 MiB sequential write / read | 6.0 / 11.1 MB/s |
| 1 GiB sequential write / read | 5.7 / 10.9 MB/s |
| Rename 1,000 files | 21.2 ops/sec |
| Metadata delete | 12.8 ops/sec |
| Mixed file operations | 35.5 ops/sec |
| SHA-256 verify | PASS |
| Errors | 0 |
| Benchmark artifacts | cleaned automatically |

Started at `2026-09-20T02:01:39.5297010+08:00` and completed at
`2026-09-20T02:20:16.5633517+08:00`.

Use this committed result as the comparison baseline for later performance or
regression work. The raw JSON report was written locally to
`C:\Temp\3waSshDrive-benchmark-full.json`.
