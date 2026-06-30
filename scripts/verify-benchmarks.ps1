param(
    [string]$Configuration = 'Release',
    [string[]]$SmokeFilter = @(),
    [switch]$RunSmoke,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = [System.IO.Path]::Combine(
    $repoRoot,
    'benchmarks',
    'UmkaSharp.Benchmarks',
    'UmkaSharp.Benchmarks.csproj')
$expectedBenchmarksPath = [System.IO.Path]::Combine(
    $repoRoot,
    'benchmarks',
    'UmkaSharp.Benchmarks',
    'expected-benchmarks.txt')
$smokeBenchmarksPath = [System.IO.Path]::Combine(
    $repoRoot,
    'benchmarks',
    'UmkaSharp.Benchmarks',
    'smoke-benchmarks.txt')

if (-not (Test-Path -LiteralPath $expectedBenchmarksPath)) {
    throw "Cannot find benchmark expected list '$expectedBenchmarksPath'."
}

if (-not (Test-Path -LiteralPath $smokeBenchmarksPath)) {
    throw "Cannot find benchmark smoke list '$smokeBenchmarksPath'."
}

if (-not $NoBuild) {
    dotnet build $project --configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Benchmark project build failed with exit code $LASTEXITCODE."
    }
}

$listArgs = @(
    'run',
    '--project', $project,
    '--configuration', $Configuration,
    '--no-build',
    '--',
    '--list', 'flat'
)

$rawOutput = & dotnet @listArgs 2>&1
$runExitCode = $LASTEXITCODE
$benchmarkNames = @($rawOutput | ForEach-Object { $_.ToString() })
$benchmarkNames | ForEach-Object { Write-Host $_ }

if ($runExitCode -ne 0) {
    throw "Benchmark discovery failed with exit code $runExitCode."
}

$expectedBenchmarks = Get-Content -LiteralPath $expectedBenchmarksPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$defaultSmokeBenchmarks = Get-Content -LiteralPath $smokeBenchmarksPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

foreach ($expectedBenchmark in $expectedBenchmarks) {
    if ($benchmarkNames -notcontains $expectedBenchmark) {
        throw "Benchmark discovery did not include expected benchmark '$expectedBenchmark'."
    }
}

foreach ($smokeBenchmark in $defaultSmokeBenchmarks) {
    if ($expectedBenchmarks -notcontains $smokeBenchmark) {
        throw "Benchmark smoke list contains '$smokeBenchmark', but expected-benchmarks.txt does not."
    }

    if ($benchmarkNames -notcontains $smokeBenchmark) {
        throw "Benchmark discovery did not include smoke benchmark '$smokeBenchmark'."
    }
}

if ($RunSmoke) {
    $smokeFilters = if ($SmokeFilter.Count -ne 0) {
        $SmokeFilter
    }
    else {
        $defaultSmokeBenchmarks
    }

    foreach ($smokeFilter in $smokeFilters) {
        if ($benchmarkNames -notcontains $smokeFilter) {
            throw "Benchmark smoke list includes unknown benchmark '$smokeFilter'."
        }
    }

    $smokeArgs = @(
        'run',
        '--project', $project,
        '--configuration', $Configuration,
        '--no-build',
        '--',
        '--filter'
    ) + $smokeFilters + @(
        '--job', 'Dry',
        '--join',
        '--disableLogFile'
    )

    $smokeOutput = & dotnet @smokeArgs 2>&1
    $smokeExitCode = $LASTEXITCODE
    $smokeOutput | ForEach-Object { Write-Host $_ }

    if ($smokeExitCode -ne 0) {
        throw "Benchmark smoke run failed with exit code $smokeExitCode."
    }

    $hasSummary = $smokeOutput | Where-Object {
        $_.ToString().Contains('Summary', [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if (-not $hasSummary) {
        throw "Benchmark smoke run did not produce a BenchmarkDotNet summary."
    }

    foreach ($smokeFilter in $smokeFilters) {
        $hasBenchmark = $smokeOutput | Where-Object {
            $_.ToString().Contains($smokeFilter, [StringComparison]::Ordinal)
        } | Select-Object -First 1
        if (-not $hasBenchmark) {
            throw "Benchmark smoke run did not mention expected benchmark '$smokeFilter'."
        }
    }
}
