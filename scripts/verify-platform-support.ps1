param(
    [string]$WorkflowPath = '.github/workflows/ci.yml',
    [string]$PackageLayoutVerifierPath = 'scripts/verify-package-layout.ps1',
    [string]$PackLocalPath = 'scripts/pack-local.ps1',
    [string]$PlatformsDocPath = 'docs/platforms.md',
    [string]$GlobalJsonPath = 'global.json',
    [string]$DirectoryBuildPropsPath = 'Directory.Build.props',
    [string]$LinuxDockerVerifierPath = 'scripts/verify-linux-docker.ps1'
)

$ErrorActionPreference = 'Stop'

function Assert-ContainsText {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Message
    )

    if (-not $Text.Contains($Expected, [StringComparison]::Ordinal)) {
        throw $Message
    }
}

function Assert-DoesNotContainText {
    param(
        [string]$Text,
        [string]$Unexpected,
        [string]$Message
    )

    if ($Text.Contains($Unexpected, [StringComparison]::Ordinal)) {
        throw $Message
    }
}

function Read-RequiredText {
    param([string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "Cannot find '$resolvedPath'."
    }

    return [System.IO.File]::ReadAllText($resolvedPath)
}

$supportedRids = @('win-x64', 'linux-x64')
$candidateRids = @('linux-arm64', 'osx-x64', 'osx-arm64')

$workflowText = Read-RequiredText -Path $WorkflowPath
$packageLayoutText = Read-RequiredText -Path $PackageLayoutVerifierPath
$packLocalText = Read-RequiredText -Path $PackLocalPath
$platformsDocText = Read-RequiredText -Path $PlatformsDocPath
$globalJsonText = Read-RequiredText -Path $GlobalJsonPath
$directoryBuildPropsText = Read-RequiredText -Path $DirectoryBuildPropsPath
$linuxDockerVerifierText = Read-RequiredText -Path $LinuxDockerVerifierPath

$globalJson = $globalJsonText | ConvertFrom-Json
if ($globalJson.sdk.version -notmatch '^9\.0\.') {
    throw "global.json must pin a .NET 9 SDK version, but found '$($globalJson.sdk.version)'."
}

if ($globalJson.sdk.rollForward -ne 'latestFeature') {
    throw "global.json should use rollForward 'latestFeature' for .NET 9 SDK servicing."
}

$directoryBuildProps = [xml]$directoryBuildPropsText
$targetFramework = $directoryBuildProps.Project.PropertyGroup.TargetFramework | Select-Object -First 1
if ($targetFramework -ne 'net9.0') {
    throw "Directory.Build.props must set TargetFramework to 'net9.0', but found '$targetFramework'."
}

Assert-ContainsText -Text $workflowText -Expected "dotnet-version: '9.0.x'" -Message "Workflow setup-dotnet steps must use .NET 9."
Assert-ContainsText -Text $linuxDockerVerifierText -Expected "mcr.microsoft.com/dotnet/sdk:9.0" -Message "Linux Docker verifier must use the .NET 9 SDK image."

foreach ($rid in $supportedRids) {
    Assert-ContainsText -Text $workflowText -Expected "rid: $rid" -Message "Workflow native build matrix must include '$rid'."
    Assert-ContainsText -Text $packageLayoutText -Expected "'$rid'" -Message "Package layout verifier default RID list must include '$rid'."
    Assert-ContainsText -Text $packLocalText -Expected "'$rid'" -Message "Local pack script must mention supported RID '$rid'."
    Assert-ContainsText -Text $platformsDocText -Expected "| ``$rid`` |" -Message "Platforms doc must list '$rid' as supported."
}

Assert-ContainsText -Text $packLocalText -Expected "'umka_shim.dll'" -Message "Local pack script must know the Windows native asset name."
Assert-ContainsText -Text $packLocalText -Expected "'libumka_shim.so'" -Message "Local pack script must know the Linux native asset name."

foreach ($rid in $candidateRids) {
    Assert-ContainsText -Text $platformsDocText -Expected "### ``$rid``" -Message "Platforms doc must include candidate section '$rid'."
    Assert-DoesNotContainText -Text $workflowText -Unexpected "rid: $rid" -Message "Workflow must not build unsupported candidate RID '$rid'."
}

foreach ($candidateRid in $candidateRids) {
    $defaultRidListPattern = "\[string\[\]\]\`$RequiredRuntimeIdentifiers = @\((?s).*'$([regex]::Escape($candidateRid))'"
    if ($packageLayoutText -match $defaultRidListPattern) {
        throw "Package layout verifier must not require unsupported candidate RID '$candidateRid' by default."
    }
}

Assert-ContainsText `
    -Text $packageLayoutText `
    -Expected 'unexpected runtime native asset' `
    -Message 'Package layout verifier must reject unexpected runtime native assets.'
