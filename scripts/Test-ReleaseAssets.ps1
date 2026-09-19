[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Directory,
    [Parameter(Mandatory = $true)][string]$Tag,
    [switch]$Remote
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directoryPath = (Resolve-Path -LiteralPath $Directory).Path
if (-not (Test-Path -LiteralPath $directoryPath -PathType Container)) {
    throw "Release asset directory does not exist: $Directory"
}

$resolver = Join-Path $PSScriptRoot 'Resolve-ReleaseVersion.ps1'
$version = (& $resolver -Tag $Tag | ConvertFrom-Json)
$packageVersion = $version.PackageVersion

$requiredAssetNames = @(
    '3waSshDrive-Setup.exe'
    "3waSshDrive-$packageVersion-full.nupkg"
    'releases.win.json'
    'assets.win.json'
    "3waSshDrive-win-x64-$Tag.zip"
)

foreach ($assetName in $requiredAssetNames) {
    $assetPath = Join-Path $directoryPath $assetName
    if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
        throw "Missing release asset: $assetName"
    }
}

$targetFullPackages = @(Get-ChildItem `
        -LiteralPath $directoryPath `
        -File |
        Where-Object { $_.Name -like "*-$packageVersion-full.nupkg" })
if ($targetFullPackages.Count -ne 1) {
    throw "Expected exactly one full package for $packageVersion; found $($targetFullPackages.Count)."
}

$targetDeltaPackages = @(Get-ChildItem `
        -LiteralPath $directoryPath `
        -File |
        Where-Object { $_.Name -like "*-$packageVersion-delta.nupkg" })
if ($targetDeltaPackages.Count -gt 1) {
    throw "Expected at most one delta package for $packageVersion; found $($targetDeltaPackages.Count)."
}

$checksumAssetNames = @($requiredAssetNames)
if ($targetDeltaPackages.Count -eq 1) {
    $checksumAssetNames += $targetDeltaPackages[0].Name
}
$checksumAssetNames = @($checksumAssetNames | Sort-Object -Unique)

$checksumLines = foreach ($assetName in $checksumAssetNames) {
    $hash = (Get-FileHash `
            -LiteralPath (Join-Path $directoryPath $assetName) `
            -Algorithm SHA256).Hash.ToUpperInvariant()
    "$hash  $assetName"
}

$checksumName = 'SHA256SUMS.txt'
$checksumPath = Join-Path $directoryPath $checksumName
Set-Content `
    -LiteralPath $checksumPath `
    -Value $checksumLines `
    -Encoding ascii

if ($Remote) {
    $remoteJson = & gh release view $Tag --json isDraft,assets
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect remote draft release '$Tag'. Exit code: $LASTEXITCODE"
    }

    $remoteRelease = $remoteJson | ConvertFrom-Json
    if ($remoteRelease.isDraft -ne $true) {
        throw "Remote release '$Tag' must remain a draft until validation completes."
    }

    $remoteAssetNames = @($remoteRelease.assets | ForEach-Object { $_.name })
    foreach ($assetName in @($checksumAssetNames + $checksumName)) {
        if ($remoteAssetNames -notcontains $assetName) {
            throw "Remote draft is missing release asset: $assetName"
        }
    }
}

Write-Host "Release assets validated: $directoryPath"
