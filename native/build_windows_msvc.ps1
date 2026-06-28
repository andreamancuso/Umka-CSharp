param(
    [string]$UmkaRoot = $(if ($env:UMKA_ROOT) { $env:UMKA_ROOT } else { 'C:\dev\umka-lang' }),
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$umkaSrc = Join-Path $UmkaRoot 'src'
$apiHeader = Join-Path $umkaSrc 'umka_api.h'
if (-not (Test-Path -LiteralPath $apiHeader)) {
    throw "Cannot find Umka sources at '$UmkaRoot'. Set UMKA_ROOT to a checkout containing src\umka_api.h."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildDir = Join-Path $PSScriptRoot "build\$Configuration"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$vsRoots = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Community',
    'C:\Program Files\Microsoft Visual Studio\2022\Professional',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise',
    'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools'
)

$vcvars = $null
foreach ($root in $vsRoots) {
    $candidate = Join-Path $root 'VC\Auxiliary\Build\vcvars64.bat'
    if (Test-Path -LiteralPath $candidate) {
        $vcvars = $candidate
        break
    }
}

if (-not $vcvars) {
    throw 'Could not find vcvars64.bat for Visual Studio 2022.'
}

$umkaSources = @(
    'umka_api.c',
    'umka_common.c',
    'umka_compiler.c',
    'umka_const.c',
    'umka_decl.c',
    'umka_expr.c',
    'umka_gen.c',
    'umka_ident.c',
    'umka_lexer.c',
    'umka_runtime.c',
    'umka_stmt.c',
    'umka_types.c',
    'umka_vm.c'
) | ForEach-Object { Join-Path $umkaSrc $_ }

$shimSource = Join-Path $PSScriptRoot 'umka_shim.c'
$outDll = Join-Path $buildDir 'umka_shim.dll'
$outLib = Join-Path $buildDir 'umka_shim.lib'

$optimization = if ($Configuration -eq 'Debug') { '/Od /Zi' } else { '/O2' }
$sources = @($shimSource) + $umkaSources
$sourceArgs = ($sources | ForEach-Object { '"' + $_ + '"' }) -join ' '

$command = "cl /nologo $optimization /LD /MD /DUMKA_BUILD /DUMKA_EXT_LIBS /I `"$umkaSrc`" $sourceArgs /Fe:`"$outDll`" /link /IMPLIB:`"$outLib`""
$cmd = "`"$vcvars`" >nul && cd /d `"$buildDir`" && $command"

cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed with exit code $LASTEXITCODE."
}

Write-Host "Built $outDll"
