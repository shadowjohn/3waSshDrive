[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$GitHubEnv
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$pattern = '^v(?<year>\d{4})\.(?<month>\d{2})\.(?<day>\d{2})\.(?<revision>\d{2})$'
if ($Tag -notmatch $pattern) {
    throw "Invalid release tag: $Tag"
}

$year = [int]$Matches.year
$month = [int]$Matches.month
$day = [int]$Matches.day
$revision = [int]$Matches.revision

if ($revision -lt 1 -or $revision -gt 99) {
    throw 'Revision must be 01-99.'
}

[void][datetime]::new($year, $month, $day)

$result = [pscustomobject]@{
    DisplayVersion = $Tag
    PackageVersion = "$year.$($month * 100 + $day).$revision"
    AssemblyVersion = "$year.$month.$day.$revision"
}

if ($GitHubEnv) {
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_DISPLAY_VERSION=$($result.DisplayVersion)"
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_PACKAGE_VERSION=$($result.PackageVersion)"
    Add-Content -LiteralPath $GitHubEnv -Value "THREEWA_ASSEMBLY_VERSION=$($result.AssemblyVersion)"
}

$result | ConvertTo-Json -Compress
