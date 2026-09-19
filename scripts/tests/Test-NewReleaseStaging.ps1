[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stager = Join-Path $PSScriptRoot '..\New-ReleaseStaging.ps1'
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

function Write-File {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $path = Join-Path $Root (ConvertTo-NativePath $RelativePath)
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force |
        Out-Null
    Set-Content -LiteralPath $path -Value "fixture: $RelativePath"
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    '3waSshDrive-ReleaseStagingTest-' + [guid]::NewGuid().ToString('N'))

try {
    $source = Join-Path $testRoot 'source'
    $staged = Join-Path $testRoot 'staged'
    New-Item -ItemType Directory -Path $source -Force | Out-Null

    foreach ($relativePath in $requiredFiles) {
        Write-File -Root $source -RelativePath $relativePath
    }
    foreach ($relativePath in @(
        '3waSshDrive.exe.config',
        'BouncyCastle.Cryptography.dll',
        'Assets\avatar_prompt.txt',
        '3waSshDrive.pdb',
        'Renci.SshNet.xml',
        'notes.txt',
        'runtime\unexpected.json'
    )) {
        Write-File -Root $source -RelativePath $relativePath
    }

    $null = & $stager `
        -SourceDirectory $source `
        -OutputDirectory $staged

    foreach ($relativePath in @(
        $requiredFiles +
        '3waSshDrive.exe.config' +
        'BouncyCastle.Cryptography.dll'
    )) {
        $stagedPath = Join-Path $staged (ConvertTo-NativePath $relativePath)
        if (-not (Test-Path -LiteralPath $stagedPath -PathType Leaf)) {
            throw "Release staging is missing allowed file: $relativePath"
        }
    }

    foreach ($relativePath in @(
        'Assets\avatar_prompt.txt',
        '3waSshDrive.pdb',
        'Renci.SshNet.xml',
        'notes.txt',
        'runtime\unexpected.json'
    )) {
        $stagedPath = Join-Path $staged (ConvertTo-NativePath $relativePath)
        if (Test-Path -LiteralPath $stagedPath) {
            throw "Release staging contains excluded file: $relativePath"
        }
    }

    $notEmptyFailure = $false
    try {
        $null = & $stager `
            -SourceDirectory $source `
            -OutputDirectory $staged
    }
    catch {
        $notEmptyFailure = $_.Exception.Message -match 'must be empty'
    }
    if (-not $notEmptyFailure) {
        throw 'Release staging must reject a non-empty output directory.'
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Release staging script tests passed.'
