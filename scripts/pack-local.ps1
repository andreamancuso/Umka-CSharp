param(
    [string]$Version = '0.1.0-local',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$winShim = Join-Path $repoRoot "native\build\Release\umka_shim.dll"
if (-not (Test-Path -LiteralPath $winShim)) {
    & (Join-Path $repoRoot 'native\build_windows_msvc.ps1') -Configuration Release
}

$runtimeDir = Join-Path $repoRoot 'runtimes\win-x64\native'
New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
Copy-Item -LiteralPath $winShim -Destination (Join-Path $runtimeDir 'umka_shim.dll') -Force

$linuxShimCandidates = @(
    (Join-Path $repoRoot 'native\build-linux\libumka_shim.so'),
    (Join-Path $repoRoot 'native\build\libumka_shim.so')
)
$linuxShim = $linuxShimCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($linuxShim) {
    $linuxRuntimeDir = Join-Path $repoRoot 'runtimes\linux-x64\native'
    New-Item -ItemType Directory -Force -Path $linuxRuntimeDir | Out-Null
    Copy-Item -LiteralPath $linuxShim -Destination (Join-Path $linuxRuntimeDir 'libumka_shim.so') -Force
}
else {
    Write-Warning "Linux native asset was not found. Run scripts\verify-linux-docker.ps1 before packing to include linux-x64."
}

dotnet pack (Join-Path $repoRoot 'src\UmkaSharp\UmkaSharp.csproj') `
    --configuration $Configuration `
    /p:Version=$Version `
    --output (Join-Path $repoRoot 'artifacts\packages')
