param(
    [string]$Version = '0.1.0-local',
    [string]$Configuration = 'Release',
    [string]$PackageDirectory = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$packageDirectoryProvided = -not [string]::IsNullOrWhiteSpace($PackageDirectory)
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $repoRoot 'artifacts\packages'
}

$packageDir = [System.IO.Path]::GetFullPath($PackageDirectory)
$packagePath = Join-Path $packageDir "UmkaSharp.$Version.nupkg"
if (-not (Test-Path -LiteralPath $packagePath)) {
    if ($packageDirectoryProvided) {
        throw "Cannot find package '$packagePath'."
    }

    & (Join-Path $repoRoot 'scripts\pack-local.ps1') -Version $Version -Configuration $Configuration
}

$artifactsDir = Join-Path $repoRoot 'artifacts'
$smokeDir = Join-Path $artifactsDir 'package-smoke'
$resolvedArtifactsDir = [System.IO.Path]::GetFullPath($artifactsDir)
$resolvedSmokeDir = [System.IO.Path]::GetFullPath($smokeDir)
if (-not $resolvedSmokeDir.StartsWith($resolvedArtifactsDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to modify a smoke-test directory outside '$resolvedArtifactsDir'."
}

dotnet new console --framework net9.0 --output $smokeDir --force | Out-Null
dotnet add (Join-Path $smokeDir 'package-smoke.csproj') package UmkaSharp --version $Version --source $packageDir | Out-Null

$program = @'
using UmkaSharp;

using var runtime = UmkaRuntime.FromSource("""
    import "host.um"

    fn answer*(): int {
        return host::doubleIt(21)
    }
    """);

runtime.AddModule("host.um", "fn doubleIt*(x: int): int");
runtime.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
runtime.Compile();

Console.WriteLine(runtime.GetFunction("answer").CallInt64());
'@

Set-Content -LiteralPath (Join-Path $smokeDir 'Program.cs') -Value $program
dotnet run --project (Join-Path $smokeDir 'package-smoke.csproj') --configuration $Configuration
