param(
    [string]$UmkaRoot = $(if ($env:UMKA_ROOT) { $env:UMKA_ROOT } else { 'C:\dev\umka-lang' }),
    [string]$Image = 'mcr.microsoft.com/dotnet/sdk:9.0',
    [string]$Configuration = 'Release',
    [switch]$SkipApt
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$umkaApiHeader = Join-Path $UmkaRoot 'src\umka_api.h'
if (-not (Test-Path -LiteralPath $umkaApiHeader)) {
    throw "Cannot find Umka sources at '$UmkaRoot'. Set -UmkaRoot or UMKA_ROOT to a checkout containing src\umka_api.h."
}

$bash = @'
set -euo pipefail

if [ "${SKIP_APT:-0}" != "1" ]; then
    apt-get update
    DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends cmake build-essential
fi

dotnet --version
rm -rf native/build-linux
cmake -S native -B native/build-linux -DCMAKE_BUILD_TYPE="${CONFIGURATION}"
cmake --build native/build-linux --config "${CONFIGURATION}"

mkdir -p native/build
cp native/build-linux/libumka_shim.so native/build/libumka_shim.so

dotnet test UmkaSharp.sln --configuration "${CONFIGURATION}" --verbosity minimal

mkdir -p runtimes/linux-x64/native
cp native/build-linux/libumka_shim.so runtimes/linux-x64/native/libumka_shim.so
ls -l runtimes/linux-x64/native/libumka_shim.so
'@

$dockerArgs = @(
    'run',
    '--rm',
    '-e',
    'UMKA_ROOT=/umka',
    '-e',
    "CONFIGURATION=$Configuration"
)

if ($SkipApt) {
    $dockerArgs += @('-e', 'SKIP_APT=1')
}

$dockerArgs += @(
    '-v',
    "${repoRoot}:/work",
    '-v',
    "${UmkaRoot}:/umka:ro",
    '-w',
    '/work',
    $Image,
    'bash',
    '-lc',
    $bash
)

& docker @dockerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Linux Docker verification failed with exit code $LASTEXITCODE."
}
