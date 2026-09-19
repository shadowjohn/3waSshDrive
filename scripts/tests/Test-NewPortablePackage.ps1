[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packager = Join-Path $PSScriptRoot '..\New-PortablePackage.ps1'
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

function Write-PackageFixture {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [string]$Omit
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    foreach ($relativePath in $requiredFiles) {
        if ($relativePath -eq $Omit) {
            continue
        }

        $path = Join-Path $Directory (ConvertTo-NativePath $relativePath)
        New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
        Set-Content -LiteralPath $path -Value 'portable test fixture'
    }

    Set-Content -LiteralPath (Join-Path $Directory '3waSshDrive.exe.config') -Value 'config fixture'
    Set-Content -LiteralPath (Join-Path $Directory 'symbols.pdb') -Value 'excluded'
    Set-Content -LiteralPath (Join-Path $Directory 'documentation.xml') -Value 'excluded'
    Set-Content -LiteralPath (Join-Path $Directory 'old-package.zip') -Value 'excluded'
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    '3waSshDrive-PortableTest-' + [guid]::NewGuid().ToString('N'))

try {
    $source = Join-Path $testRoot 'source'
    $output = Join-Path $testRoot 'output'
    $expanded = Join-Path $testRoot 'expanded'
    Write-PackageFixture -Directory $source
    New-Item -ItemType Directory -Path $output -Force | Out-Null

    $null = & $packager `
        -SourceDirectory $source `
        -OutputDirectory $output `
        -Label 'test123'

    $zip = Join-Path $output '3waSshDrive-win-x64-test123.zip'
    $checksum = "$zip.sha256"
    if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
        throw "Portable ZIP was not created: $zip"
    }
    if (-not (Test-Path -LiteralPath $checksum -PathType Leaf)) {
        throw "Portable checksum was not created: $checksum"
    }

    $expectedChecksum = "{0}  {1}" -f `
        (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash, `
        (Split-Path $zip -Leaf)
    $actualChecksum = (Get-Content -LiteralPath $checksum -Raw).Trim()
    if ($actualChecksum -ne $expectedChecksum) {
        throw "Checksum mismatch. Expected '$expectedChecksum', got '$actualChecksum'."
    }

    Expand-Archive -LiteralPath $zip -DestinationPath $expanded
    foreach ($relativePath in $requiredFiles) {
        $expandedPath = Join-Path $expanded (ConvertTo-NativePath $relativePath)
        if (-not (Test-Path -LiteralPath $expandedPath -PathType Leaf)) {
            throw "Portable ZIP is missing required file: $relativePath"
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $expanded '3waSshDrive.exe.config'))) {
        throw 'Portable ZIP did not include the complete build output.'
    }
    foreach ($excludedFile in @('symbols.pdb', 'documentation.xml', 'old-package.zip')) {
        if (Test-Path -LiteralPath (Join-Path $expanded $excludedFile)) {
            throw "Portable ZIP contains excluded file: $excludedFile"
        }
    }

    $missingSource = Join-Path $testRoot 'missing-source'
    Write-PackageFixture -Directory $missingSource -Omit 'Velopack.dll'
    $threw = $false
    try {
        $null = & $packager `
            -SourceDirectory $missingSource `
            -OutputDirectory $output `
            -Label 'missing'
    }
    catch {
        $threw = $true
        if ($_.Exception.Message -notlike '*Missing package file: Velopack.dll*') {
            throw "Unexpected missing-file error: $($_.Exception.Message)"
        }
    }

    if (-not $threw) {
        throw 'Packaging should fail when Velopack.dll is missing.'
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$buildScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'build.bat') -Raw
$runScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'run.bat') -Raw

foreach ($requiredBuildFragment in @(
    'set "MSBUILD_VERSION_ARGS="',
    'if defined THREEWA_DISPLAY_VERSION if defined THREEWA_PACKAGE_VERSION if defined THREEWA_ASSEMBLY_VERSION (',
    'set "MSBUILD_VERSION_ARGS=/p:ThreeWaDisplayVersion=%THREEWA_DISPLAY_VERSION% /p:ThreeWaPackageVersion=%THREEWA_PACKAGE_VERSION% /p:ThreeWaAssemblyVersion=%THREEWA_ASSEMBLY_VERSION%"'
)) {
    if (-not $buildScript.Contains($requiredBuildFragment)) {
        throw "build.bat is missing version injection: $requiredBuildFragment"
    }
}

$versionArgumentUses = [regex]::Matches(
    $buildScript,
    '(?im)^dotnet (test|build) .*%MSBUILD_VERSION_ARGS%\s*$')
if ($versionArgumentUses.Count -ne 2) {
    throw 'build.bat must pass MSBUILD_VERSION_ARGS to both test and App build.'
}

foreach ($requiredRunFragment in @(
    'echo [3waSshDrive] Running self-check: %APP_PATH%',
    'start "" /wait "%APP_PATH%" --self-check',
    'echo [3waSshDrive] Self-check passed.'
)) {
    if (-not $runScript.Contains($requiredRunFragment)) {
        throw "run.bat is missing executable self-check behavior: $requiredRunFragment"
    }
}

if ($runScript.Contains('echo [3waSshDrive] Ready: %APP_PATH%')) {
    throw 'run.bat --check still uses the file-existence-only check.'
}

Write-Host 'Portable package script tests passed.'
