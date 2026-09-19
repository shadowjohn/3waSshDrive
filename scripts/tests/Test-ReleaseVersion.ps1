[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolver = Join-Path $PSScriptRoot '..\Resolve-ReleaseVersion.ps1'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

function Resolve-TestVersion {
    param([Parameter(Mandatory = $true)][string]$Tag)

    (& $resolver -Tag $Tag | ConvertFrom-Json)
}

$first = Resolve-TestVersion -Tag 'v2026.09.19.01'
Assert-Equal 'v2026.09.19.01' $first.DisplayVersion 'Display version mismatch.'
Assert-Equal '2026.919.1' $first.PackageVersion 'Package version mismatch.'
Assert-Equal '2026.9.19.1' $first.AssemblyVersion 'Assembly version mismatch.'

$second = Resolve-TestVersion -Tag 'v2026.12.31.02'
Assert-Equal '2026.1231.2' $second.PackageVersion 'December package version mismatch.'
Assert-Equal '2026.12.31.2' $second.AssemblyVersion 'December assembly version mismatch.'

foreach ($invalidTag in @(
    'v2026.02.30.01',
    'v2026.09.19.00',
    '2026.09.19.01',
    'v2026.9.19.01'
)) {
    $threw = $false
    try {
        $null = Resolve-TestVersion -Tag $invalidTag
    }
    catch {
        $threw = $true
    }

    if (-not $threw) {
        throw "Expected invalid tag '$invalidTag' to throw."
    }
}

$environmentFile = Join-Path ([System.IO.Path]::GetTempPath()) ("3waSshDrive-env-{0}.txt" -f [guid]::NewGuid().ToString('N'))
try {
    $null = & $resolver -Tag 'v2026.09.19.01' -GitHubEnv $environmentFile
    $environmentLines = Get-Content -LiteralPath $environmentFile

    foreach ($expectedLine in @(
        'THREEWA_DISPLAY_VERSION=v2026.09.19.01',
        'THREEWA_PACKAGE_VERSION=2026.919.1',
        'THREEWA_ASSEMBLY_VERSION=2026.9.19.1'
    )) {
        if ($environmentLines -notcontains $expectedLine) {
            throw "GitHub environment output is missing '$expectedLine'."
        }
    }
}
finally {
    Remove-Item -LiteralPath $environmentFile -Force -ErrorAction SilentlyContinue
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$appProjectPath = Join-Path $repositoryRoot 'src\3waSshDrive.App\3waSshDrive.App.csproj'
$mainFormPath = Join-Path $repositoryRoot 'src\3waSshDrive.App\MainForm.cs'
$projectText = Get-Content -LiteralPath $appProjectPath -Raw
$mainFormText = Get-Content -LiteralPath $mainFormPath -Raw

foreach ($requiredProjectFragment in @(
    '<ThreeWaDisplayVersion Condition="''$(ThreeWaDisplayVersion)'' == ''''">v2026.09.19.01</ThreeWaDisplayVersion>',
    '<ThreeWaPackageVersion Condition="''$(ThreeWaPackageVersion)'' == ''''">2026.919.1</ThreeWaPackageVersion>',
    '<ThreeWaAssemblyVersion Condition="''$(ThreeWaAssemblyVersion)'' == ''''">2026.9.19.1</ThreeWaAssemblyVersion>',
    '<Version>$(ThreeWaPackageVersion)</Version>',
    '<AssemblyVersion>$(ThreeWaAssemblyVersion)</AssemblyVersion>',
    '<FileVersion>$(ThreeWaAssemblyVersion)</FileVersion>',
    '<InformationalVersion>$(ThreeWaDisplayVersion)</InformationalVersion>',
    '<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>',
    '<PlatformTarget>x64</PlatformTarget>',
    '<Prefer32Bit>false</Prefer32Bit>',
    '<AssemblyMetadata Include="VelopackPackageVersion" Value="$(ThreeWaPackageVersion)" />'
)) {
    if (-not $projectText.Contains($requiredProjectFragment)) {
        throw "App project is missing version metadata fragment: $requiredProjectFragment"
    }
}

if (-not $mainFormText.Contains('Text = $"3waSshDrive - {Application.ProductVersion}";')) {
    throw 'MainForm title must use Application.ProductVersion.'
}

if ($mainFormText.Contains('AppVersion')) {
    throw 'MainForm must not contain or reference a hard-coded AppVersion.'
}

Write-Host 'ReleaseVersion script tests passed.'
