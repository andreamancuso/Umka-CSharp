param(
    [string]$Version = '0.1.0-local',
    [string]$Configuration = 'Release',
    [string]$UmkaRoot = $(if ($env:UMKA_ROOT) { $env:UMKA_ROOT } else { 'C:\dev\umka-lang' })
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed with exit code $LASTEXITCODE."
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
& ([System.IO.Path]::Combine($repoRoot, 'scripts', 'verify-third-party-notices.ps1')) -UmkaRoot $UmkaRoot

$runningOnWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
    [System.Runtime.InteropServices.OSPlatform]::Windows)

$winShim = [System.IO.Path]::Combine($repoRoot, 'native', 'build', 'Release', 'umka_shim.dll')
if ($runningOnWindows -and -not (Test-Path -LiteralPath $winShim)) {
    & ([System.IO.Path]::Combine($repoRoot, 'native', 'build_windows_msvc.ps1')) -Configuration Release -UmkaRoot $UmkaRoot
}

if (Test-Path -LiteralPath $winShim) {
    $runtimeDir = [System.IO.Path]::Combine($repoRoot, 'runtimes', 'win-x64', 'native')
    New-Item -ItemType Directory -Force -Path $runtimeDir | Out-Null
    Copy-Item -LiteralPath $winShim -Destination (Join-Path $runtimeDir 'umka_shim.dll') -Force
}
else {
    Write-Warning "Windows native asset was not found. Build the Windows shim before packing to include win-x64."
}

$linuxShimCandidates = @(
    ([System.IO.Path]::Combine($repoRoot, 'native', 'build-linux', 'libumka_shim.so')),
    ([System.IO.Path]::Combine($repoRoot, 'native', 'build', 'libumka_shim.so'))
)
$linuxShim = $linuxShimCandidates |
    Where-Object { Test-Path -LiteralPath $_ } |
    ForEach-Object { Get-Item -LiteralPath $_ } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1
if ($linuxShim) {
    $linuxRuntimeDir = [System.IO.Path]::Combine($repoRoot, 'runtimes', 'linux-x64', 'native')
    New-Item -ItemType Directory -Force -Path $linuxRuntimeDir | Out-Null
    Copy-Item -LiteralPath $linuxShim.FullName -Destination (Join-Path $linuxRuntimeDir 'libumka_shim.so') -Force
}
else {
    Write-Warning "Linux native asset was not found. Run scripts\verify-linux-docker.ps1 before packing to include linux-x64."
}

Invoke-DotNet pack ([System.IO.Path]::Combine($repoRoot, 'src', 'UmkaSharp', 'UmkaSharp.csproj')) `
    --configuration $Configuration `
    /p:Version=$Version `
    --output ([System.IO.Path]::Combine($repoRoot, 'artifacts', 'packages'))
