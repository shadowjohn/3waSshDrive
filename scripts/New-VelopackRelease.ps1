[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BuildDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$PackageVersion,
    [Parameter(Mandatory = $true)][string]$ReleaseNotes,
    [string]$RepositoryUrl = 'https://github.com/shadowjohn/3waSshDrive',
    [string]$GitHubToken,
    [switch]$WhatIfArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE"
    }
}

function Get-RepositoryName {
    param([Parameter(Mandatory = $true)][string]$Url)

    try {
        $uri = [uri]$Url
    }
    catch {
        throw "Invalid GitHub repository URL: $Url"
    }

    if (-not $uri.IsAbsoluteUri -or $uri.Host -ne 'github.com') {
        throw "Invalid GitHub repository URL: $Url"
    }

    $segments = $uri.AbsolutePath.Trim('/').Split(
        '/',
        [StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -ne 2) {
        throw "Invalid GitHub repository URL: $Url"
    }

    "$($segments[0])/$($segments[1])"
}

function Set-NormalizedSetupName {
    param(
        [Parameter(Mandatory = $true)][string]$ReleaseDirectory,
        [Parameter(Mandatory = $true)][string]$AssetsPath
    )

    $normalizedName = '3waSshDrive-Setup.exe'
    $normalizedPath = Join-Path $ReleaseDirectory $normalizedName
    $generatedName = '3waSshDrive-win-Setup.exe'
    $generatedPath = Join-Path $ReleaseDirectory $generatedName
    $assets = @(Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json)
    $generatedAssets = @($assets | Where-Object {
            $_.RelativeFileName -eq $generatedName
        })
    $normalizedAssets = @($assets | Where-Object {
            $_.RelativeFileName -eq $normalizedName
        })

    if (Test-Path -LiteralPath $normalizedPath -PathType Leaf) {
        if ($normalizedAssets.Count -eq 1 -and $generatedAssets.Count -eq 0) {
            return
        }
        if ($normalizedAssets.Count -ne 0 -or $generatedAssets.Count -ne 1) {
            throw 'Velopack Setup asset manifest is inconsistent with the normalized file.'
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $generatedPath -PathType Leaf)) {
            throw "Missing Velopack Setup executable: $generatedName"
        }
        if ($normalizedAssets.Count -ne 0 -or $generatedAssets.Count -ne 1) {
            throw "Velopack asset manifest does not contain exactly one '$generatedName'."
        }

        Move-Item -LiteralPath $generatedPath -Destination $normalizedPath
    }

    $generatedAssets[0].RelativeFileName = $normalizedName
    ConvertTo-Json -InputObject $assets -Compress -Depth 10 |
        Set-Content -LiteralPath $AssetsPath -Encoding utf8
}

if ($PackageVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid Velopack package version: $PackageVersion"
}

$buildPath = (Resolve-Path -LiteralPath $BuildDirectory).Path
if (-not (Test-Path -LiteralPath $buildPath -PathType Container)) {
    throw "Build directory does not exist: $BuildDirectory"
}

$releaseNotesPath = (Resolve-Path -LiteralPath $ReleaseNotes).Path
if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) {
    throw "Release notes file does not exist: $ReleaseNotes"
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = (Resolve-Path -LiteralPath $OutputDirectory).Path

$packArguments = @(
    'pack'
    '--packId'; '3waSshDrive'
    '--packVersion'; $PackageVersion
    '--packDir'; $buildPath
    '--mainExe'; '3waSshDrive.exe'
    '--packTitle'; '3waSshDrive'
    '--runtime'; 'win-x64'
    '--channel'; 'win'
    '--instLocation'; 'PerUser'
    '--noPortable'
    '--releaseNotes'; $releaseNotesPath
    '--outputDir'; $outputPath
)

if (-not [string]::IsNullOrWhiteSpace($env:VELOPACK_SIGN_PARAMS)) {
    $packArguments += @('--signParams', $env:VELOPACK_SIGN_PARAMS)
}

if ($WhatIfArguments) {
    $packArguments
    return
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$toolsDirectory = Join-Path $repositoryRoot '.tools'
$vpkPath = Join-Path $toolsDirectory 'vpk.exe'

if (-not (Test-Path -LiteralPath $vpkPath -PathType Leaf)) {
    New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
    Invoke-CheckedCommand `
        -FilePath 'dotnet' `
        -ArgumentList @(
            'tool'; 'install'
            '--tool-path'; $toolsDirectory
            'vpk'
            '--version'; '1.2.0'
        ) `
        -FailureMessage 'vpk install failed.'
}

$versionOutput = (& $vpkPath --version | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch '^1\.2\.0(?:\+.*)?$') {
    throw "Unexpected vpk version: $versionOutput"
}

$repositoryName = Get-RepositoryName -Url $RepositoryUrl
$releaseList = & gh release list `
    --repo $repositoryName `
    --exclude-drafts `
    --exclude-pre-releases `
    --limit 1 `
    --json tagName
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query prior stable GitHub releases. Exit code: $LASTEXITCODE"
}

$priorStableReleases = @($releaseList | ConvertFrom-Json)
$hasPriorStableRelease = $priorStableReleases.Count -gt 0
if ($hasPriorStableRelease) {
    $downloadArguments = @(
        'download'; 'github'
        '--repoUrl'; $RepositoryUrl
        '--channel'; 'win'
        '--outputDir'; $outputPath
    )
    if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
        $downloadArguments += @('--token', $GitHubToken)
    }

    Invoke-CheckedCommand `
        -FilePath $vpkPath `
        -ArgumentList $downloadArguments `
        -FailureMessage 'Previous release download failed.'
}

Invoke-CheckedCommand `
    -FilePath $vpkPath `
    -ArgumentList $packArguments `
    -FailureMessage 'Velopack release packaging failed.'

$assetsPath = Join-Path $outputPath 'assets.win.json'
$releasesPath = Join-Path $outputPath 'releases.win.json'
$fullPackagePath = Join-Path $outputPath "3waSshDrive-$PackageVersion-full.nupkg"

foreach ($requiredPath in @($assetsPath, $releasesPath, $fullPackagePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Missing Velopack release asset: $(Split-Path $requiredPath -Leaf)"
    }
}

Set-NormalizedSetupName `
    -ReleaseDirectory $outputPath `
    -AssetsPath $assetsPath

if ($hasPriorStableRelease) {
    $deltaPackagePath = Join-Path $outputPath "3waSshDrive-$PackageVersion-delta.nupkg"
    if (-not (Test-Path -LiteralPath $deltaPackagePath -PathType Leaf)) {
        throw "Missing Velopack delta package: $(Split-Path $deltaPackagePath -Leaf)"
    }
}

Write-Host "Velopack release assets: $outputPath"
