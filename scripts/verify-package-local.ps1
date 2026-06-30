param(
    [string]$Version = '0.1.0-local',
    [string]$Configuration = 'Release',
    [string]$PackageDirectory = '',
    [string[]]$RequiredRuntimeIdentifiers = @(),
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
$packageDirectoryProvided = -not [string]::IsNullOrWhiteSpace($PackageDirectory)
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = [System.IO.Path]::Combine($repoRoot, 'artifacts', 'packages')
}

$packageDir = [System.IO.Path]::GetFullPath($PackageDirectory)
$packagePath = Join-Path $packageDir "UmkaSharp.$Version.nupkg"
if (-not $packageDirectoryProvided) {
    & ([System.IO.Path]::Combine($repoRoot, 'scripts', 'pack-local.ps1')) -Version $Version -Configuration $Configuration -UmkaRoot $UmkaRoot
}

& ([System.IO.Path]::Combine($repoRoot, 'scripts', 'verify-third-party-notices.ps1')) -UmkaRoot $UmkaRoot

if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Cannot find package '$packagePath'."
}

if ($RequiredRuntimeIdentifiers.Count -eq 0) {
    $supportedRuntimeAssets = [ordered]@{
        'win-x64' = [System.IO.Path]::Combine($repoRoot, 'runtimes', 'win-x64', 'native', 'umka_shim.dll')
        'linux-x64' = [System.IO.Path]::Combine($repoRoot, 'runtimes', 'linux-x64', 'native', 'libumka_shim.so')
    }

    $inferredRuntimeIdentifiers = [System.Collections.Generic.List[string]]::new()
    foreach ($entry in $supportedRuntimeAssets.GetEnumerator()) {
        if (Test-Path -LiteralPath $entry.Value) {
            $inferredRuntimeIdentifiers.Add($entry.Key)
        }
    }

    $currentRuntimeIdentifier = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        'win-x64'
    }
    elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        'linux-x64'
    }
    else {
        ''
    }

    if (-not [string]::IsNullOrWhiteSpace($currentRuntimeIdentifier) -and -not $inferredRuntimeIdentifiers.Contains($currentRuntimeIdentifier)) {
        $inferredRuntimeIdentifiers.Add($currentRuntimeIdentifier)
    }

    $RequiredRuntimeIdentifiers = $inferredRuntimeIdentifiers.ToArray()
}

& ([System.IO.Path]::Combine($repoRoot, 'scripts', 'verify-package-layout.ps1')) `
    -PackagePath $packagePath `
    -ExpectedVersion $Version `
    -RequiredRuntimeIdentifiers $RequiredRuntimeIdentifiers `
    -RequireSymbols

$artifactsDir = [System.IO.Path]::Combine($repoRoot, 'artifacts')
$smokeDir = [System.IO.Path]::Combine($artifactsDir, 'package-smoke')
$sampleDir = [System.IO.Path]::Combine($repoRoot, 'samples', 'PackageConsumer')
$sampleProject = Join-Path $sampleDir 'PackageConsumer.csproj'
$sampleExpectedOutput = Join-Path $sampleDir 'expected-output.txt'
if (-not (Test-Path -LiteralPath $sampleProject)) {
    throw "Cannot find package consumer sample '$sampleProject'."
}
if (-not (Test-Path -LiteralPath $sampleExpectedOutput)) {
    throw "Cannot find package consumer expected output '$sampleExpectedOutput'."
}

$expectedOutput = @(
    Get-Content -LiteralPath $sampleExpectedOutput |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
if ($expectedOutput.Count -eq 0) {
    throw "Package consumer expected output '$sampleExpectedOutput' is empty."
}

$seenExpectedOutput = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($expectedLine in $expectedOutput) {
    if (-not $seenExpectedOutput.Add($expectedLine)) {
        throw "Package consumer expected output '$sampleExpectedOutput' contains duplicate line '$expectedLine'."
    }
}

$resolvedArtifactsDir = [System.IO.Path]::GetFullPath($artifactsDir)
$resolvedSmokeDir = [System.IO.Path]::GetFullPath($smokeDir)
$pathComparison = if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    [StringComparison]::OrdinalIgnoreCase
}
else {
    [StringComparison]::Ordinal
}
$pathSeparators = @(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$resolvedArtifactsRoot = $resolvedArtifactsDir.TrimEnd($pathSeparators) + [System.IO.Path]::DirectorySeparatorChar

if ($resolvedSmokeDir -ne $resolvedArtifactsDir -and -not $resolvedSmokeDir.StartsWith($resolvedArtifactsRoot, $pathComparison)) {
    throw "Refusing to modify a smoke-test directory outside '$resolvedArtifactsDir'."
}

if (Test-Path -LiteralPath $smokeDir) {
    Remove-Item -LiteralPath $smokeDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $smokeDir | Out-Null
Copy-Item -LiteralPath $sampleProject -Destination $smokeDir -Force
Copy-Item -LiteralPath (Join-Path $sampleDir 'Program.cs') -Destination $smokeDir -Force

$smokeProject = Join-Path $smokeDir 'PackageConsumer.csproj'
[xml]$projectXml = Get-Content -LiteralPath $smokeProject
$packageReference = $projectXml.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'UmkaSharp' } | Select-Object -First 1
if (-not $packageReference) {
    throw "Package consumer sample does not reference UmkaSharp."
}

$packageReference.Version = $Version
$projectXml.Save($smokeProject)

$packageCacheDir = Join-Path $smokeDir '.nuget-packages'
New-Item -ItemType Directory -Force -Path $packageCacheDir | Out-Null

$previousNuGetPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = $packageCacheDir
try {
    Invoke-DotNet restore $smokeProject --source $packageDir --packages $packageCacheDir
    $rawRunOutput = dotnet run --project $smokeProject --configuration $Configuration --no-restore 2>&1
    $runExitCode = $LASTEXITCODE
    $runOutput = @($rawRunOutput | ForEach-Object { $_.ToString() })
    $runOutput | ForEach-Object { Write-Host $_ }
    if ($runExitCode -ne 0) {
        throw "dotnet command failed with exit code $runExitCode."
    }

    foreach ($expectedLine in $expectedOutput) {
        if ($runOutput -notcontains $expectedLine) {
            throw "Package consumer output did not contain expected line '$expectedLine'."
        }
    }
}
finally {
    if ($null -eq $previousNuGetPackages) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $previousNuGetPackages
    }
}
