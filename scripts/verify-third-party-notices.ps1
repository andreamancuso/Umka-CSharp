param(
    [string]$UmkaRoot = $(if ($env:UMKA_ROOT) { $env:UMKA_ROOT } else { 'C:\dev\umka-lang' }),
    [string]$NoticesPath = ''
)

$ErrorActionPreference = 'Stop'

function Normalize-NoticeText {
    param([string]$Text)

    return ($Text -replace "`r`n", "`n").Trim()
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($NoticesPath)) {
    $NoticesPath = Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md'
}

$umkaLicensePath = Join-Path $UmkaRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $umkaLicensePath)) {
    throw "Cannot find Umka license at '$umkaLicensePath'. Set -UmkaRoot or UMKA_ROOT to a checkout containing LICENSE."
}

if (-not (Test-Path -LiteralPath $NoticesPath)) {
    throw "Cannot find third-party notices at '$NoticesPath'."
}

$umkaLicense = Normalize-NoticeText -Text ([System.IO.File]::ReadAllText($umkaLicensePath))
$notices = Normalize-NoticeText -Text ([System.IO.File]::ReadAllText($NoticesPath))

if (-not $notices.Contains('Upstream: <https://github.com/vtereshkov/umka-lang>')) {
    throw "Third-party notices must identify the upstream Umka repository."
}

if (-not $notices.Contains('Selected source: <https://github.com/andreamancuso/umka-lang>')) {
    throw "Third-party notices must identify the selected Umka source repository used for CI/package builds."
}

if (-not $notices.Contains('License: BSD 2-Clause License')) {
    throw "Third-party notices must identify Umka as BSD 2-Clause licensed."
}

if (-not $notices.Contains($umkaLicense)) {
    throw "Third-party notices must include the full Umka LICENSE text from '$umkaLicensePath'."
}
