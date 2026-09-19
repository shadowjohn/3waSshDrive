[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packager = Join-Path $PSScriptRoot '..\New-VelopackRelease.ps1'
$assetValidator = Join-Path $PSScriptRoot '..\Test-ReleaseAssets.ps1'
$installedReleaseTester = Join-Path $PSScriptRoot '..\Test-InstalledRelease.ps1'
$updateRestartTester = Join-Path $PSScriptRoot '..\Test-UpdateRestart.ps1'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$releaseWorkflowPath = Join-Path $repositoryRoot '.github\workflows\windows-release.yml'

function Assert-ContainsArgument {
    param(
        [Parameter(Mandatory = $true)][object[]]$ActualArguments,
        [Parameter(Mandatory = $true)][string]$Expected
    )

    if ($ActualArguments -notcontains $Expected) {
        throw "vpk arguments are missing '$Expected'."
    }
}

function Assert-ArgumentPair {
    param(
        [Parameter(Mandatory = $true)][object[]]$ActualArguments,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $index = [Array]::IndexOf($ActualArguments, $Name)
    if ($index -lt 0 -or $index + 1 -ge $ActualArguments.Count) {
        throw "vpk arguments are missing '$Name'."
    }
    if ($ActualArguments[$index + 1] -ne $Value) {
        throw "Expected '$Name' value '$Value', got '$($ActualArguments[$index + 1])'."
    }
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    '3waSshDrive-VelopackTest-' + [guid]::NewGuid().ToString('N'))
$buildDirectory = Join-Path $testRoot 'build'
$outputDirectory = Join-Path $testRoot 'release'
$releaseNotes = Join-Path $testRoot 'release-notes.md'
$previousSignParams = [Environment]::GetEnvironmentVariable(
    'VELOPACK_SIGN_PARAMS',
    [EnvironmentVariableTarget]::Process)

try {
    New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
    Set-Content -LiteralPath $releaseNotes -Value 'Release test notes.'
    Remove-Item Env:VELOPACK_SIGN_PARAMS -ErrorAction SilentlyContinue

    $unsignedArguments = @(& $packager `
        -BuildDirectory $buildDirectory `
        -OutputDirectory $outputDirectory `
        -PackageVersion '2026.919.1' `
        -ReleaseNotes $releaseNotes `
        -WhatIfArguments)

    $resolvedBuildDirectory = (Resolve-Path -LiteralPath $buildDirectory).Path
    Assert-ContainsArgument $unsignedArguments 'pack'
    Assert-ArgumentPair $unsignedArguments '--packId' '3waSshDrive'
    Assert-ArgumentPair $unsignedArguments '--packVersion' '2026.919.1'
    Assert-ArgumentPair $unsignedArguments '--packDir' $resolvedBuildDirectory
    Assert-ArgumentPair $unsignedArguments '--mainExe' '3waSshDrive.exe'
    Assert-ArgumentPair $unsignedArguments '--packTitle' '3waSshDrive'
    Assert-ArgumentPair $unsignedArguments '--runtime' 'win-x64'
    Assert-ArgumentPair $unsignedArguments '--channel' 'win'
    Assert-ArgumentPair $unsignedArguments '--instLocation' 'PerUser'
    Assert-ContainsArgument $unsignedArguments '--noPortable'
    if ($unsignedArguments -contains '--signParams') {
        throw 'Unsigned vpk arguments must not contain --signParams.'
    }

    $env:VELOPACK_SIGN_PARAMS = '/fd SHA256'
    $signedArguments = @(& $packager `
        -BuildDirectory $buildDirectory `
        -OutputDirectory $outputDirectory `
        -PackageVersion '2026.919.1' `
        -ReleaseNotes $releaseNotes `
        -WhatIfArguments)
    Assert-ArgumentPair $signedArguments '--signParams' '/fd SHA256'

    $assetDirectory = Join-Path $testRoot 'assets'
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    $expectedAssetNames = @(
        '3waSshDrive-Setup.exe'
        '3waSshDrive-2026.919.1-full.nupkg'
        '3waSshDrive-2026.919.1-delta.nupkg'
        'releases.win.json'
        'assets.win.json'
        '3waSshDrive-win-x64-v2026.09.19.01.zip'
    )
    foreach ($assetName in $expectedAssetNames) {
        Set-Content `
            -LiteralPath (Join-Path $assetDirectory $assetName) `
            -Value "test asset: $assetName"
    }

    & $assetValidator `
        -Directory $assetDirectory `
        -Tag 'v2026.09.19.01'

    $checksumPath = Join-Path $assetDirectory 'SHA256SUMS.txt'
    if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        throw 'Asset validation must create SHA256SUMS.txt.'
    }

    $checksumLines = @(Get-Content -LiteralPath $checksumPath)
    if ($checksumLines.Count -ne $expectedAssetNames.Count) {
        throw "Expected $($expectedAssetNames.Count) checksum lines, got $($checksumLines.Count)."
    }
    foreach ($assetName in $expectedAssetNames) {
        if (-not ($checksumLines -match "^[A-F0-9]{64}  $([regex]::Escape($assetName))$")) {
            throw "Missing uppercase SHA-256 entry for '$assetName'."
        }
    }
    if ($checksumLines -match 'SHA256SUMS\.txt') {
        throw 'SHA256SUMS.txt must not checksum itself.'
    }

    Remove-Item -LiteralPath (Join-Path $assetDirectory 'assets.win.json')
    $missingAssetFailed = $false
    try {
        & $assetValidator `
            -Directory $assetDirectory `
            -Tag 'v2026.09.19.01'
    }
    catch {
        $missingAssetFailed = $_.Exception.Message -match 'assets\.win\.json'
    }
    if (-not $missingAssetFailed) {
        throw 'Asset validation must reject a missing assets.win.json.'
    }

    $installedReleaseSource = (
        Get-Content -LiteralPath $installedReleaseTester -Raw
    ) -replace "`r`n", "`n"
    $requiredInstalledReleaseFragments = @(
        "Join-Path `$env:LOCALAPPDATA '3waSshDrive'"
        'Install root already exists:'
        "-ArgumentList '--silent'"
        "Join-Path `$root '3waSshDrive.exe'"
        "-ArgumentList '--self-check'"
        '[System.Diagnostics.FileVersionInfo]::GetVersionInfo'
        'ProductVersion'
        'FileVersion'
        'ExpectedPackageVersion'
        '[string]$AdditionalSmokeScript'
        '-InstalledExe'
        '-LockPath'
        'finally {'
        "Join-Path `$root 'Update.exe'"
        "-ArgumentList '--silent uninstall'"
        'AddSeconds(30)'
        'Uninstall left install root:'
    )
    foreach ($fragment in $requiredInstalledReleaseFragments) {
        if (-not $installedReleaseSource.Contains($fragment)) {
            throw "Installed release smoke script is missing: $fragment"
        }
    }
    if ($installedReleaseSource -match 'Assembly\.(Load|LoadFrom|LoadFile)') {
        throw 'Installed release smoke must not reflection-load the installed executable.'
    }

    $updateRestartSource = (
        Get-Content -LiteralPath $updateRestartTester -Raw
    ) -replace "`r`n", "`n"
    $requiredRestartFragments = @(
        '[string]$InstalledExe'
        '[string]$LockPath'
        '999999'
        '1..2 | ForEach-Object'
        "-ArgumentList '--self-check'"
        '[System.IO.FileMode]::OpenOrCreate'
        '[System.IO.FileAccess]::ReadWrite'
        '[System.IO.FileShare]::None'
        'Self-check was blocked by live lock handle.'
        '$handle.Dispose()'
    )
    foreach ($fragment in $requiredRestartFragments) {
        if (-not $updateRestartSource.Contains($fragment)) {
            throw "Update restart smoke script is missing: $fragment"
        }
    }

    $releaseWorkflow = (
        Get-Content -LiteralPath $releaseWorkflowPath -Raw
    ) -replace "`r`n", "`n"
    $requiredWorkflowFragments = @(
        'name: Windows release'
        "      - 'v[0-9][0-9][0-9][0-9].[0-9][0-9].[0-9][0-9].[0-9][0-9]'"
        'group: release-${{ github.ref }}'
        'cancel-in-progress: false'
        'runs-on: windows-2022'
        'contents: write'
        'dotnet-version: 10.0.x'
        'dotnet tool install --tool-path .\.tools vpk --version 1.2.0'
        '.\scripts\tests\Test-ReleaseVersion.ps1'
        '.\scripts\tests\Test-NewPortablePackage.ps1'
        '.\scripts\tests\Test-NewVelopackRelease.ps1'
        '-Tag $env:GITHUB_REF_NAME -GitHubEnv $env:GITHUB_ENV'
        'call build.bat --no-pause'
        'call run.bat --check'
        '-Label $env:GITHUB_REF_NAME'
        'releases/generate-notes'
        '.\scripts\New-VelopackRelease.ps1'
        '.\scripts\Test-ReleaseAssets.ps1'
        'gh release view $env:GITHUB_REF_NAME --json isDraft,assets'
        '$PSNativeCommandUseErrorActionPreference = $false'
        'gh release delete-asset'
        '.\.tools\vpk.exe upload github'
        '--merge'
        'gh release upload'
        '.\artifacts\release\releases.win.json'
        'assets.win.json'
        '-Remote'
        '.\scripts\Test-InstalledRelease.ps1'
        '-AdditionalSmokeScript .\scripts\Test-UpdateRestart.ps1'
        'gh release edit $env:GITHUB_REF_NAME --draft=false --latest'
        'uses: actions/upload-artifact@v4'
        'if: always()'
    )
    foreach ($fragment in $requiredWorkflowFragments) {
        if (-not $releaseWorkflow.Contains($fragment)) {
            throw "Windows release workflow is missing: $fragment"
        }
    }

    $orderedReleaseSteps = @(
        '- name: Checkout source and pinned dependencies'
        '- name: Install .NET SDK'
        '- name: Install pinned Velopack CLI'
        '- name: Test release scripts'
        '- name: Resolve release version'
        '- name: Build and test release'
        '- name: Run release executable self-check'
        '- name: Package tag-named portable build'
        '- name: Generate release notes'
        '- name: Package Velopack release'
        '- name: Validate local release assets'
        '- name: Prepare draft release for idempotent upload'
        '- name: Upload Velopack draft assets'
        '- name: Upload supplemental draft assets'
        '- name: Validate remote draft assets'
        '- name: Smoke test installed release'
        '- name: Publish verified release'
        '- name: Upload release diagnostics'
    )
    $previousIndex = -1
    foreach ($stepName in $orderedReleaseSteps) {
        $index = $releaseWorkflow.IndexOf($stepName, [StringComparison]::Ordinal)
        if ($index -le $previousIndex) {
            throw "Windows release workflow step is missing or out of order: $stepName"
        }
        $previousIndex = $index
    }

    if ($releaseWorkflow -match '(?m)^\s+--publish\s*$') {
        throw 'Velopack upload must leave the release as a draft until smoke tests pass.'
    }
    if ($releaseWorkflow -match 'microsoft\.com/.*/submission') {
        throw 'Microsoft submission must remain a manual post-release step.'
    }
}
finally {
    if ($null -eq $previousSignParams) {
        Remove-Item Env:VELOPACK_SIGN_PARAMS -ErrorAction SilentlyContinue
    }
    else {
        $env:VELOPACK_SIGN_PARAMS = $previousSignParams
    }
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Velopack release script tests passed.'
