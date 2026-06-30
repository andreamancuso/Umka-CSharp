param(
    [string]$WorkflowPath = '.github/workflows/ci.yml',
    [string]$PackageConsumerExpectedOutputPath = 'samples/PackageConsumer/expected-output.txt'
)

$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-JobBlock {
    param(
        [string[]]$Lines,
        [string]$JobName
    )

    $start = -1
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match "^  $([regex]::Escape($JobName)):\s*$") {
            $start = $i
            break
        }
    }

    Assert-Condition ($start -ge 0) "Cannot find workflow job '$JobName'."

    $end = $Lines.Count
    for ($i = $start + 1; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^  [A-Za-z0-9_-]+:\s*$') {
            $end = $i
            break
        }
    }

    return @{
        Start = $start
        End = $end
        Lines = $Lines[$start..($end - 1)]
    }
}

$resolvedWorkflowPath = [System.IO.Path]::GetFullPath($WorkflowPath)
if (-not (Test-Path -LiteralPath $resolvedWorkflowPath)) {
    throw "Cannot find workflow '$resolvedWorkflowPath'."
}

$resolvedExpectedOutputPath = [System.IO.Path]::GetFullPath($PackageConsumerExpectedOutputPath)
if (-not (Test-Path -LiteralPath $resolvedExpectedOutputPath)) {
    throw "Cannot find package consumer expected output '$resolvedExpectedOutputPath'."
}

$text = [System.IO.File]::ReadAllText($resolvedWorkflowPath)
$lines = Get-Content -LiteralPath $resolvedWorkflowPath
$expectedOutputLines = Get-Content -LiteralPath $resolvedExpectedOutputPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

Assert-Condition ($text -match '(?m)^  release:\s*$') "Workflow must declare a release event."
Assert-Condition ($text -match '(?m)^    types:\s*\[published\]\s*$') "Workflow release event must be limited to published releases."
Assert-Condition ($text -notmatch '(?m)^\s+tags:\s*') "Workflow must not run from tag pushes."

$publishBlock = Get-JobBlock -Lines $lines -JobName 'publish'
$publishText = $publishBlock.Lines -join "`n"
$packageSmokeBlock = Get-JobBlock -Lines $lines -JobName 'package-smoke'
$packageSmokeText = $packageSmokeBlock.Lines -join "`n"
$testBlock = Get-JobBlock -Lines $lines -JobName 'test'
$testText = $testBlock.Lines -join "`n"

Assert-Condition `
    ($publishText -match "(?m)^    if:\s*github\.event_name == 'release' && github\.event\.action == 'published'\s*$") `
    "Publish job must run only when github.event_name is release and github.event.action is published."

Assert-Condition `
    ($publishText -match '(?m)^    needs:\s*\[workflow-policy,\s*pack,\s*package-smoke\]\s*$') `
    "Publish job must depend on workflow-policy, pack, and package-smoke."

Assert-Condition `
    ($publishText.Contains('NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}')) `
    "Publish job must expose the NUGET_API_KEY secret as an environment variable."

Assert-Condition `
    ($publishText -match '(?s)- name: Verify NuGet API key.*NUGET_API_KEY repository secret is required for release publishing') `
    "Publish job must fail clearly when the NUGET_API_KEY secret is missing."

$allPushIndexes = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains('dotnet nuget push')) {
        $allPushIndexes += $i
    }
}

Assert-Condition ($allPushIndexes.Count -eq 2) "Workflow should contain exactly two dotnet nuget push commands."

$publishPushIndexes = $allPushIndexes | Where-Object {
    $_ -ge $publishBlock.Start -and $_ -lt $publishBlock.End
}

Assert-Condition ($publishPushIndexes.Count -eq 2) "All dotnet nuget push commands must be inside the publish job."
Assert-Condition ($publishText -match 'dotnet nuget push nupkg/\*\.nupkg') "Publish job must push the nupkg."
Assert-Condition ($publishText -match 'dotnet nuget push nupkg/\*\.snupkg') "Publish job must push the snupkg."

Assert-Condition ($packageSmokeText -match 'os:\s*\[windows-latest,\s*ubuntu-latest\]') "Package smoke job must run on Windows and Ubuntu."
Assert-Condition ($packageSmokeText -match '\$runOutput\s*=\s*dotnet run') "Package smoke job must capture consumer output."
Assert-Condition ($packageSmokeText -match '\$runOutput\s*-notcontains\s*\$expectedLine') "Package smoke job must reject missing expected output lines."
Assert-Condition ($packageSmokeText.Contains('samples/PackageConsumer/expected-output.txt')) "Package smoke job must read the package consumer expected-output fixture."
foreach ($expectedPrefix in @('score=', 'host=', 'try-host=', 'exports=', 'dynamic=', 'negative=', 'unsupported=', 'strings=', 'warning=', 'fs=')) {
    $matchingExpectedLines = @($expectedOutputLines | Where-Object { $_.StartsWith($expectedPrefix, [System.StringComparison]::Ordinal) })
    Assert-Condition ($matchingExpectedLines.Count -gt 0) "Package consumer expected-output fixture must contain a line starting with '$expectedPrefix'."
}

Assert-Condition ($testText -match 'verify-benchmarks\.ps1 -Configuration Release -NoBuild') "Test job must verify benchmark harness discovery."
Assert-Condition ($testText -match 'verify-samples\.ps1 -Configuration Release -NoBuild') "Test job must run project-reference sample output checks."
Assert-Condition (([System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath('scripts/verify-benchmarks.ps1'))).Contains('expected-benchmarks.txt')) "Benchmark verifier must read the expected benchmark fixture."
Assert-Condition (([System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath('scripts/verify-benchmarks.ps1'))).Contains('smoke-benchmarks.txt')) "Benchmark verifier must read the smoke benchmark fixture."
Assert-Condition (([System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath('scripts/verify-samples.ps1'))).Contains('expected-output.txt')) "Project-reference sample verifier must read per-sample expected-output fixtures."
Assert-Condition (([System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath('scripts/verify-samples.ps1'))).Contains('project-reference-samples.txt')) "Project-reference sample verifier must read the sample list fixture."

foreach ($pushIndex in $publishPushIndexes) {
    $windowEnd = [Math]::Min($pushIndex + 5, $lines.Count - 1)
    $commandWindow = $lines[$pushIndex..$windowEnd] -join "`n"
    Assert-Condition ($commandWindow -match '--api-key "\$NUGET_API_KEY"') "NuGet push must use the NUGET_API_KEY environment variable."
    Assert-Condition ($commandWindow -match '--source https://api\.nuget\.org/v3/index\.json') "NuGet push must target NuGet.org."
    Assert-Condition ($commandWindow -match '--skip-duplicate') "NuGet push should skip duplicate packages."
}
