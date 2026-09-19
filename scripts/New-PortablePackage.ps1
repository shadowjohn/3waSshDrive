[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SourceDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$Label
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
    'runtime\winfsp-2.1.25156-manifest.json',
    'Assets\background.png',
    'Assets\header_logo.png'
)

function ConvertTo-NativePath {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $RelativePath.Replace(
        '\',
        [System.IO.Path]::DirectorySeparatorChar)
}

if ($Label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$') {
    throw "Invalid portable package label: $Label"
}

$sourcePath = (Resolve-Path -LiteralPath $SourceDirectory).Path
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Source directory does not exist: $SourceDirectory"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path

foreach ($relativePath in $requiredFiles) {
    $requiredPath = Join-Path $sourcePath (ConvertTo-NativePath $relativePath)
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing package file: $relativePath"
    }

    $null = Resolve-Path -LiteralPath $requiredPath
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) (
    '3waSshDrive-portable-' + [guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    foreach ($file in Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Force) {
        $extension = $file.Extension.ToLowerInvariant()
        if ($extension -in @('.pdb', '.xml', '.zip', '.nupkg')) {
            continue
        }

        $relativePath = [System.IO.Path]::GetRelativePath(
            $sourcePath,
            $file.FullName)
        $destination = Join-Path $stage $relativePath
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force |
            Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }

    $zip = Join-Path $outputPath "3waSshDrive-win-x64-$Label.zip"
    Compress-Archive `
        -Path (Join-Path $stage '*') `
        -DestinationPath $zip `
        -Force

    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Set-Content `
        -LiteralPath "$zip.sha256" `
        -Value "$hash  $(Split-Path $zip -Leaf)" `
        -Encoding ascii

    Write-Host "Portable package: $zip"
    Write-Host "SHA-256: $hash"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
