param(
    [string]$Configuration = 'Release',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$sampleListPath = [System.IO.Path]::Combine($repoRoot, 'samples', 'project-reference-samples.txt')
if (-not (Test-Path -LiteralPath $sampleListPath)) {
    throw "Cannot find project-reference sample list '$sampleListPath'."
}

$samples = @(
    Get-Content -LiteralPath $sampleListPath |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($samples.Count -eq 0) {
    throw "Project-reference sample list '$sampleListPath' is empty."
}

$seenSamples = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($sample in $samples) {
    if ($sample -match '[\\/]' -or $sample -eq '.' -or $sample -eq '..') {
        throw "Project-reference sample entry '$sample' must be a simple sample directory name."
    }

    if (-not $seenSamples.Add($sample)) {
        throw "Project-reference sample list contains duplicate sample '$sample'."
    }
}

foreach ($sample in $samples) {
    $project = [System.IO.Path]::Combine(
        $repoRoot,
        'samples',
        $sample,
        "$sample.csproj")
    $expectedOutputPath = [System.IO.Path]::Combine(
        $repoRoot,
        'samples',
        $sample,
        'expected-output.txt')

    if (-not (Test-Path -LiteralPath $expectedOutputPath)) {
        throw "Sample '$sample' is missing expected output file '$expectedOutputPath'."
    }

    if (-not (Test-Path -LiteralPath $project)) {
        throw "Sample '$sample' is missing project file '$project'."
    }

    $expectedOutput = @(
        Get-Content -LiteralPath $expectedOutputPath |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($expectedOutput.Count -eq 0) {
        throw "Sample '$sample' expected output file '$expectedOutputPath' is empty."
    }

    Write-Host "Running sample: $sample"

    $runArgs = @(
        'run',
        '--project', $project,
        '--configuration', $Configuration
    )
    if ($NoBuild) {
        $runArgs += '--no-build'
    }

    $rawOutput = & dotnet @runArgs 2>&1
    $runExitCode = $LASTEXITCODE
    $runOutput = @($rawOutput | ForEach-Object { $_.ToString() })
    $runOutput | ForEach-Object { Write-Host $_ }

    if ($runExitCode -ne 0) {
        throw "Sample '$sample' failed with exit code $runExitCode."
    }

    foreach ($expectedLine in $expectedOutput) {
        if ($runOutput -notcontains $expectedLine) {
            throw "Sample '$sample' output did not contain expected line '$expectedLine'."
        }
    }
}
