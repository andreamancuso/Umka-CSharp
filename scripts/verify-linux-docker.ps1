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

& (Join-Path $repoRoot 'scripts\verify-third-party-notices.ps1') -UmkaRoot $UmkaRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI was not found. Install Docker Desktop, then rerun scripts\verify-linux-docker.ps1."
}

& docker version --format '{{.Server.Version}}' *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Docker daemon is not available. Start Docker Desktop, then rerun scripts\verify-linux-docker.ps1."
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

PACKAGE_VERSION=0.1.0-local
PACKAGE_DIR=artifacts/packages-linux
SMOKE_DIR=artifacts/linux-package-smoke
PACKAGE_CACHE="${SMOKE_DIR}/.nuget-packages"

rm -rf "${PACKAGE_DIR}" "${SMOKE_DIR}"
mkdir -p "${PACKAGE_DIR}" "${SMOKE_DIR}" "${PACKAGE_CACHE}"

dotnet pack src/UmkaSharp/UmkaSharp.csproj \
    --configuration "${CONFIGURATION}" \
    /p:Version="${PACKAGE_VERSION}" \
    --output "${PACKAGE_DIR}"

cp samples/PackageConsumer/PackageConsumer.csproj "${SMOKE_DIR}/"
cp samples/PackageConsumer/Program.cs "${SMOKE_DIR}/"

NUGET_PACKAGES="${PACKAGE_CACHE}" dotnet restore "${SMOKE_DIR}/PackageConsumer.csproj" \
    --source "${PACKAGE_DIR}" \
    --packages "${PACKAGE_CACHE}"

run_output="$(NUGET_PACKAGES="${PACKAGE_CACHE}" dotnet run \
    --project "${SMOKE_DIR}/PackageConsumer.csproj" \
    --configuration "${CONFIGURATION}" \
    --no-restore)"
printf '%s\n' "${run_output}"

while IFS= read -r expected_line; do
    expected_line="${expected_line%$'\r'}"
    if [ -z "${expected_line}" ]; then
        continue
    fi

    if ! grep -Fxq "${expected_line}" <<< "${run_output}"; then
        printf 'Package consumer output did not contain expected line %s.\n' "${expected_line}" >&2
        exit 1
    fi
done < samples/PackageConsumer/expected-output.txt
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
