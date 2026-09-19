[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$requiredFiles = @(
    '3waSshDrive.exe',
    '3waSshDrive.Core.dll',
    '3waSshDrive.FileSystem.dll',
    '3waSshDrive.Sftp.dll',
    '3waSshDrive.WinFsp.dll',
    'Renci.SshNet.dll',
    'Velopack.dll',
    'Newtonsoft.Json.dll',
    'runtime\winfsp-2.1.25156-manifest.json'
)

$runtimeAssets = @(
    'Assets\app.ico',
    'Assets\background.jpg',
    'Assets\background.png',
    'Assets\header_logo.png',
    'Assets\mascot.jpg',
    'Assets\mascot.mp4',
    'Assets\mascot2.mp4',
    'Assets\mascot_card.png'
)

function ConvertTo-NativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $RelativePath.Replace(
        '\',
        [System.IO.Path]::DirectorySeparatorChar)
}

function Test-ReleaseFileAllowed {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    if ($requiredFiles -contains $RelativePath -or
        $runtimeAssets -contains $RelativePath) {
        return $true
    }

    if ($RelativePath -notmatch '[\\/]') {
        return [System.IO.Path]::GetExtension($RelativePath) -in @(
            '.exe',
            '.dll',
            '.config'
        )
    }

    return $false
}

$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Source directory does not exist: $SourceDirectory"
}

if (Test-Path -LiteralPath $OutputDirectory) {
    $outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
    if ((Get-ChildItem -LiteralPath $outputPath -Force | Select-Object -First 1)) {
        throw "Release staging directory must be empty: $OutputDirectory"
    }
}
else {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path
}

$included = 0
$excluded = 0
foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force) {
    $relativePath = [System.IO.Path]::GetRelativePath(
        $sourcePath,
        $file.FullName).Replace('/', '\')
    if (-not (Test-ReleaseFileAllowed -RelativePath $relativePath)) {
        $excluded++
        continue
    }

    $destination = Join-Path $outputPath (ConvertTo-NativePath $relativePath)
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force |
        Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination
    $included++
}

foreach ($relativePath in @($requiredFiles + $runtimeAssets)) {
    $stagedPath = Join-Path $outputPath (ConvertTo-NativePath $relativePath)
    if (-not (Test-Path -LiteralPath $stagedPath -PathType Leaf)) {
        throw "Missing required release file: $relativePath"
    }
}

Write-Host "Release staging complete: $outputPath (included: $included, excluded: $excluded)"
