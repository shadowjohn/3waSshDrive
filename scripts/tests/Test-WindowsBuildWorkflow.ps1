[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$workflowPath = Join-Path $repositoryRoot '.github\workflows\windows-build.yml'
$workflow = Get-Content -LiteralPath $workflowPath -Raw

$requiredFragments = @(
    "permissions:`n  contents: read",
    '- name: Test release version script',
    'run: .\scripts\tests\Test-ReleaseVersion.ps1',
    '- name: Test portable package script',
    'run: .\scripts\tests\Test-NewPortablePackage.ps1',
    '- name: Test Windows build workflow contract',
    'run: .\scripts\tests\Test-WindowsBuildWorkflow.ps1',
    'run: call build.bat --no-pause',
    '- name: Run executable self-check through run.bat',
    'run: call run.bat --check',
    '- name: Set artifact label',
    'id: artifact_meta',
    "`$env:GITHUB_SHA.Substring(0, 8)",
    '- name: Package portable build',
    '-SourceDirectory .\src\3waSshDrive.App\bin\Release\net472',
    '-OutputDirectory .\artifacts\portable',
    "-Label '`$`{{ steps.artifact_meta.outputs.label }}'",
    '- name: Upload Windows x64 artifact',
    'uses: actions/upload-artifact@v4',
    'name: 3waSshDrive-win-x64-${{ steps.artifact_meta.outputs.label }}',
    'path: artifacts/portable/*',
    'if-no-files-found: error',
    'retention-days: 14'
)

foreach ($fragment in $requiredFragments) {
    if (-not $workflow.Contains($fragment)) {
        throw "Windows build workflow is missing: $fragment"
    }
}

$orderedStepNames = @(
    '- name: Test release version script',
    '- name: Test portable package script',
    '- name: Test Windows build workflow contract',
    '- name: Build and test through build.bat',
    '- name: Run executable self-check through run.bat',
    '- name: Set artifact label',
    '- name: Package portable build',
    '- name: Upload Windows x64 artifact'
)

$previousIndex = -1
foreach ($stepName in $orderedStepNames) {
    $index = $workflow.IndexOf($stepName, [StringComparison]::Ordinal)
    if ($index -le $previousIndex) {
        throw "Windows build workflow step is missing or out of order: $stepName"
    }
    $previousIndex = $index
}

Write-Host 'Windows build workflow contract tests passed.'
