param(
    [string]$RequiredDocsPath = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($RequiredDocsPath)) {
    $RequiredDocsPath = [System.IO.Path]::Combine($repoRoot, 'docs', 'required-docs.txt')
}

$requiredDocsFile = [System.IO.Path]::GetFullPath($RequiredDocsPath)
if (-not (Test-Path -LiteralPath $requiredDocsFile)) {
    throw "Cannot find required docs list '$requiredDocsFile'."
}

$docsRoot = [System.IO.Path]::Combine($repoRoot, 'docs')
$readmePath = [System.IO.Path]::Combine($repoRoot, 'README.md')
$examplesPath = [System.IO.Path]::Combine($docsRoot, 'examples.md')
$sampleListPath = [System.IO.Path]::Combine($repoRoot, 'samples', 'project-reference-samples.txt')

if (-not (Test-Path -LiteralPath $readmePath)) {
    throw "Cannot find README '$readmePath'."
}

if (-not (Test-Path -LiteralPath $examplesPath)) {
    throw "Cannot find examples doc '$examplesPath'."
}

if (-not (Test-Path -LiteralPath $sampleListPath)) {
    throw "Cannot find project-reference sample list '$sampleListPath'."
}

$requiredDocs = @(
    Get-Content -LiteralPath $requiredDocsFile |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($requiredDocs.Count -eq 0) {
    throw "Required docs list '$requiredDocsFile' is empty."
}

$seenDocs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($doc in $requiredDocs) {
    if (-not $seenDocs.Add($doc)) {
        throw "Required docs list contains duplicate entry '$doc'."
    }
}

$docsRootMarkdownFiles = @(
    Get-ChildItem -LiteralPath $docsRoot -Filter '*.md' -File |
        Select-Object -ExpandProperty Name
)
foreach ($markdownFileName in $docsRootMarkdownFiles) {
    if (-not $seenDocs.Contains($markdownFileName)) {
        throw "Docs-root Markdown file '$markdownFileName' is not listed in '$requiredDocsFile'."
    }
}

$readme = [System.IO.File]::ReadAllText($readmePath)

foreach ($doc in $requiredDocs) {
    if ($doc.Contains('\') -or $doc.Contains('/') -or -not $doc.EndsWith('.md', [StringComparison]::Ordinal)) {
        throw "Required docs entry '$doc' must be a docs-root Markdown file name."
    }

    $docPath = [System.IO.Path]::Combine($docsRoot, $doc)
    if (-not (Test-Path -LiteralPath $docPath)) {
        throw "Required doc '$doc' is missing at '$docPath'."
    }

    if ((Get-Item -LiteralPath $docPath).Length -le 0) {
        throw "Required doc '$docPath' is empty."
    }

    $readmeLink = "https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/$doc"
    if (-not $readme.Contains($readmeLink, [StringComparison]::Ordinal)) {
        throw "README must link to required doc '$doc' using '$readmeLink'."
    }
}

$markdownFiles = @($readmePath) + @($requiredDocs | ForEach-Object { [System.IO.Path]::Combine($docsRoot, $_) })
foreach ($markdownFile in $markdownFiles) {
    $markdownText = [System.IO.File]::ReadAllText($markdownFile)
    foreach ($match in [regex]::Matches($markdownText, '\[[^\]]+\]\((?<target>[^)\s]+)\)')) {
        $target = $match.Groups['target'].Value
        if ($target.StartsWith('#', [StringComparison]::Ordinal) -or
            $target.StartsWith('http://', [StringComparison]::OrdinalIgnoreCase) -or
            $target.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase) -or
            $target.StartsWith('mailto:', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $targetPath = ($target -split '#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($targetPath) -or -not $targetPath.EndsWith('.md', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $resolvedTarget = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine((Split-Path -Parent $markdownFile), $targetPath))
        if (-not (Test-Path -LiteralPath $resolvedTarget)) {
            throw "Markdown file '$markdownFile' links to missing local doc '$target'."
        }
    }
}

$examplesText = [System.IO.File]::ReadAllText($examplesPath)
$projectReferenceSamples = Get-Content -LiteralPath $sampleListPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
foreach ($sample in $projectReferenceSamples) {
    $sampleToken = '`' + $sample + '`'
    if (-not $examplesText.Contains($sampleToken, [StringComparison]::Ordinal)) {
        throw "Examples doc must describe project-reference sample '$sample'."
    }
}

if (-not $examplesText.Contains('`PackageConsumer`', [StringComparison]::Ordinal)) {
    throw "Examples doc must describe the PackageConsumer sample."
}
