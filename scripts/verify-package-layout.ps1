param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [string]$ExpectedPackageId = 'UmkaSharp',
    [string]$ExpectedVersion = '',
    [string[]]$RequiredRuntimeIdentifiers = @('win-x64', 'linux-x64'),
    [string]$ReferenceRoot = '',
    [switch]$RequireSymbols
)

$ErrorActionPreference = 'Stop'

function Get-NativeAssetName {
    param([string]$RuntimeIdentifier)

    if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::Ordinal)) {
        return 'umka_shim.dll'
    }

    if ($RuntimeIdentifier.StartsWith('linux-', [StringComparison]::Ordinal)) {
        return 'libumka_shim.so'
    }

    if ($RuntimeIdentifier.StartsWith('osx-', [StringComparison]::Ordinal)) {
        return 'libumka_shim.dylib'
    }

    throw "No native asset naming rule is defined for RID '$RuntimeIdentifier'."
}

function Get-ZipEntries {
    param([string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entries = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            [void]$entries.Add($entry.FullName)
        }

        return $entries
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryText {
    param(
        [string]$Path,
        [string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Package '$Path' is missing '$EntryName'."
        }

        $stream = $entry.Open()
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryBytes {
    param(
        [string]$Path,
        [string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Package '$Path' is missing '$EntryName'."
        }

        $stream = $entry.Open()
        $memory = [System.IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ZipEntryLength {
    param(
        [string]$Path,
        [string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry) {
            throw "Package '$Path' is missing '$EntryName'."
        }

        return $entry.Length
    }
    finally {
        $archive.Dispose()
    }
}

function Get-PortablePdbSourceLinkJson {
    param(
        [byte[]]$PdbBytes
    )

    try {
        Add-Type -AssemblyName System.Reflection.Metadata -ErrorAction Stop

        $sourceLinkKind = [Guid]'cc110556-a091-4d38-9fec-25ab9a351a6a'
        $stream = [System.IO.MemoryStream]::new()
        try {
            $stream.Write($PdbBytes, 0, $PdbBytes.Length)
            $stream.Position = 0
            $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream(
                $stream,
                [System.Reflection.Metadata.MetadataStreamOptions]::LeaveOpen)
            try {
                $reader = $provider.GetMetadataReader()
                foreach ($handle in $reader.CustomDebugInformation) {
                    $info = $reader.GetCustomDebugInformation($handle)
                    $kind = $reader.GetGuid($info.Kind)
                    if ($kind -ne $sourceLinkKind) {
                        continue
                    }

                    $bytes = $reader.GetBlobBytes($info.Value)
                    return [System.Text.Encoding]::UTF8.GetString($bytes)
                }
            }
            finally {
                $provider.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        $pdbText = [System.Text.Encoding]::UTF8.GetString($PdbBytes)
        if ($pdbText.IndexOf('"documents"', [StringComparison]::Ordinal) -ge 0) {
            return $pdbText
        }
    }

    return ''
}

function Normalize-Text {
    param([string]$Text)

    return ($Text -replace "`r`n", "`n").Trim()
}

function Assert-ZipEntry {
    param(
        [System.Collections.Generic.HashSet[string]]$Entries,
        [string]$EntryName,
        [string]$ArchivePath
    )

    if (-not $Entries.Contains($EntryName)) {
        throw "Package '$ArchivePath' is missing '$EntryName'."
    }
}

function Assert-ZipEntryHasContent {
    param(
        [string]$ArchivePath,
        [string]$EntryName
    )

    $length = Get-ZipEntryLength -Path $ArchivePath -EntryName $EntryName
    if ($length -le 0) {
        throw "Package '$ArchivePath' entry '$EntryName' is empty."
    }
}

function Assert-PackageEntriesAreAllowed {
    param(
        [System.Collections.Generic.HashSet[string]]$Entries,
        [string]$ArchivePath
    )

    $allowedExactEntries = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            '_rels/.rels',
            '[Content_Types].xml',
            'UmkaSharp.nuspec',
            'lib/net9.0/UmkaSharp.dll',
            'lib/net9.0/UmkaSharp.xml',
            'README.md',
            'LICENSE',
            'THIRD-PARTY-NOTICES.md'),
        [StringComparer]::Ordinal)

    $unexpectedEntries = @()
    foreach ($entry in $Entries) {
        if ($allowedExactEntries.Contains($entry)) {
            continue
        }

        if ($entry -match '^package/services/metadata/core-properties/[^/]+\.psmdcp$') {
            continue
        }

        if ($entry -match '^runtimes/[^/]+/native/[^/]+$') {
            continue
        }

        $unexpectedEntries += $entry
    }

    if ($unexpectedEntries.Count -ne 0) {
        throw "Package '$ArchivePath' contains unexpected entries: $($unexpectedEntries -join ', ')."
    }
}

function Assert-SymbolPackageEntriesAreAllowed {
    param(
        [System.Collections.Generic.HashSet[string]]$Entries,
        [string]$ArchivePath
    )

    $allowedExactEntries = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]@(
            '_rels/.rels',
            '[Content_Types].xml',
            'UmkaSharp.nuspec',
            'lib/net9.0/UmkaSharp.pdb'),
        [StringComparer]::Ordinal)

    $unexpectedEntries = @()
    foreach ($entry in $Entries) {
        if ($allowedExactEntries.Contains($entry)) {
            continue
        }

        if ($entry -match '^package/services/metadata/core-properties/[^/]+\.psmdcp$') {
            continue
        }

        $unexpectedEntries += $entry
    }

    if ($unexpectedEntries.Count -ne 0) {
        throw "Symbol package '$ArchivePath' contains unexpected entries: $($unexpectedEntries -join ', ')."
    }
}

function Assert-PackagedTextMatchesFile {
    param(
        [string]$ArchivePath,
        [string]$EntryName,
        [string]$ReferencePath
    )

    if (-not (Test-Path -LiteralPath $ReferencePath)) {
        throw "Cannot find reference file '$ReferencePath'."
    }

    $entryText = Normalize-Text -Text (Get-ZipEntryText -Path $ArchivePath -EntryName $EntryName)
    $referenceText = Normalize-Text -Text ([System.IO.File]::ReadAllText($ReferencePath))
    if ($entryText -ne $referenceText) {
        throw "Package '$ArchivePath' entry '$EntryName' does not match reference file '$ReferencePath'."
    }
}

function Assert-ReadmeLinksAreNuGetSafe {
    param(
        [string]$ArchivePath,
        [string]$ReadmeText
    )

    $unsafeTargets = [System.Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($ReadmeText, '\]\((?<target>(?:\./)?docs/[^)\s]+)\)')) {
        $unsafeTargets.Add($match.Groups['target'].Value)
    }

    foreach ($match in [regex]::Matches($ReadmeText, '(?m)^\[[^\]]+\]:\s*(?<target>(?:\./)?docs/\S+)')) {
        $unsafeTargets.Add($match.Groups['target'].Value)
    }

    if ($unsafeTargets.Count -ne 0) {
        throw "Package '$ArchivePath' README contains repository-relative docs links that may break on NuGet.org: $($unsafeTargets -join ', '). Use absolute GitHub links instead."
    }
}

function Assert-RuntimeNativeAssets {
    param(
        [System.Collections.Generic.HashSet[string]]$Entries,
        [string[]]$AllowedRuntimeIdentifiers,
        [string]$ArchivePath
    )

    $allowedRids = [System.Collections.Generic.HashSet[string]]::new(
        [string[]]$AllowedRuntimeIdentifiers,
        [StringComparer]::Ordinal)

    foreach ($entry in $Entries) {
        if ($entry -notmatch '^runtimes/([^/]+)/native/([^/]+)$') {
            continue
        }

        $rid = $Matches[1]
        $assetName = $Matches[2]
        if (-not $allowedRids.Contains($rid)) {
            throw "Package '$ArchivePath' contains unexpected runtime native asset '$entry'. Add RID '$rid' only after its native build and package-consumer verification exist."
        }

        $expectedAssetName = Get-NativeAssetName -RuntimeIdentifier $rid
        if ($assetName -ne $expectedAssetName) {
            throw "Package '$ArchivePath' contains native asset '$entry', but RID '$rid' should use '$expectedAssetName'."
        }
    }
}

function Get-NuspecValue {
    param(
        [System.Xml.XmlNode]$Metadata,
        [System.Xml.XmlNamespaceManager]$NamespaceManager,
        [string]$Name
    )

    $node = $Metadata.SelectSingleNode("n:$Name", $NamespaceManager)
    if ($null -eq $node) {
        return ''
    }

    return $node.InnerText.Trim()
}

function Assert-NuspecValue {
    param(
        [System.Xml.XmlNode]$Metadata,
        [System.Xml.XmlNamespaceManager]$NamespaceManager,
        [string]$Name,
        [string]$ExpectedValue
    )

    $actualValue = Get-NuspecValue -Metadata $Metadata -NamespaceManager $NamespaceManager -Name $Name
    if ($actualValue -ne $ExpectedValue) {
        throw "Package metadata '$Name' should be '$ExpectedValue', but was '$actualValue'."
    }
}

function Assert-NuspecContainsTag {
    param(
        [System.Xml.XmlNode]$Metadata,
        [System.Xml.XmlNamespaceManager]$NamespaceManager,
        [string]$Tag
    )

    $tags = (Get-NuspecValue -Metadata $Metadata -NamespaceManager $NamespaceManager -Name 'tags') -split '\s+'
    if ($Tag -notin $tags) {
        throw "Package metadata 'tags' is missing '$Tag'."
    }
}

function Assert-NuspecDependencyGroupIsEmpty {
    param(
        [System.Xml.XmlNode]$DependencyGroup,
        [System.Xml.XmlNamespaceManager]$NamespaceManager
    )

    $dependencies = $DependencyGroup.SelectNodes('n:dependency', $NamespaceManager)
    if ($null -ne $dependencies -and $dependencies.Count -ne 0) {
        $dependencyNames = @()
        foreach ($dependency in $dependencies) {
            $dependencyNames += $dependency.GetAttribute('id')
        }

        throw "Package metadata should not contain runtime package dependencies, but found: $($dependencyNames -join ', ')."
    }
}

function Assert-XmlDocMember {
    param(
        [xml]$XmlDocument,
        [string]$MemberName
    )

    $node = $XmlDocument.SelectSingleNode("/doc/members/member[@name='$MemberName']")
    if ($null -eq $node) {
        throw "Package XML docs are missing member '$MemberName'."
    }

    $summary = $node.SelectSingleNode('summary')
    if ($null -eq $summary -or [string]::IsNullOrWhiteSpace($summary.InnerText)) {
        throw "Package XML docs member '$MemberName' is missing a summary."
    }
}

function Assert-NoUnresolvedXmlDocInheritDoc {
    param(
        [xml]$XmlDocument
    )

    $nodes = $XmlDocument.SelectNodes('/doc/members/member[.//inheritdoc]')
    if ($null -eq $nodes -or $nodes.Count -eq 0) {
        return
    }

    $memberNames = @()
    foreach ($node in $nodes) {
        $memberNames += $node.GetAttribute('name')
    }

    throw "Package XML docs contain unresolved inheritdoc tags for: $($memberNames -join ', ')."
}

function Assert-SymbolPdbContainsSourceLink {
    param(
        [string]$ArchivePath,
        [string]$EntryName,
        [string]$RepositoryCommit
    )

    $pdbBytes = Get-ZipEntryBytes -Path $ArchivePath -EntryName $EntryName
    $sourceLinkJson = Get-PortablePdbSourceLinkJson -PdbBytes $pdbBytes
    if ([string]::IsNullOrWhiteSpace($sourceLinkJson)) {
        throw "Symbol package '$ArchivePath' PDB '$EntryName' does not contain Source Link data."
    }

    $expectedUrl = "https://raw.githubusercontent.com/andreamancuso/Umka-CSharp/$RepositoryCommit/"
    if ($sourceLinkJson.IndexOf('"documents"', [StringComparison]::Ordinal) -lt 0 -or
        $sourceLinkJson.IndexOf($expectedUrl, [StringComparison]::Ordinal) -lt 0) {
        throw "Symbol package '$ArchivePath' PDB '$EntryName' Source Link data must map documents to '$expectedUrl'."
    }
}

$resolvedPackagePath = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $resolvedPackagePath)) {
    throw "Cannot find package '$resolvedPackagePath'."
}

if ([string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $ReferenceRoot = Split-Path -Parent $PSScriptRoot
}

$resolvedReferenceRoot = [System.IO.Path]::GetFullPath($ReferenceRoot)

$packageEntries = Get-ZipEntries -Path $resolvedPackagePath
Assert-PackageEntriesAreAllowed -Entries $packageEntries -ArchivePath $resolvedPackagePath

$requiredPackageEntries = @(
    'UmkaSharp.nuspec',
    'lib/net9.0/UmkaSharp.dll',
    'lib/net9.0/UmkaSharp.xml',
    'README.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md'
)

foreach ($entryName in $requiredPackageEntries) {
    Assert-ZipEntry -Entries $packageEntries -EntryName $entryName -ArchivePath $resolvedPackagePath
    Assert-ZipEntryHasContent -ArchivePath $resolvedPackagePath -EntryName $entryName
}

foreach ($entryName in @('README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md')) {
    Assert-PackagedTextMatchesFile `
        -ArchivePath $resolvedPackagePath `
        -EntryName $entryName `
        -ReferencePath (Join-Path $resolvedReferenceRoot $entryName)
}

Assert-ReadmeLinksAreNuGetSafe `
    -ArchivePath $resolvedPackagePath `
    -ReadmeText (Get-ZipEntryText -Path $resolvedPackagePath -EntryName 'README.md')

foreach ($rid in $RequiredRuntimeIdentifiers) {
    $nativeAssetName = Get-NativeAssetName -RuntimeIdentifier $rid
    $entryName = "runtimes/$rid/native/$nativeAssetName"
    Assert-ZipEntry `
        -Entries $packageEntries `
        -EntryName $entryName `
        -ArchivePath $resolvedPackagePath
    Assert-ZipEntryHasContent -ArchivePath $resolvedPackagePath -EntryName $entryName
}

Assert-RuntimeNativeAssets `
    -Entries $packageEntries `
    -AllowedRuntimeIdentifiers $RequiredRuntimeIdentifiers `
    -ArchivePath $resolvedPackagePath

$xmlDocText = Get-ZipEntryText -Path $resolvedPackagePath -EntryName 'lib/net9.0/UmkaSharp.xml'
$xmlDocs = [xml]$xmlDocText
Assert-NoUnresolvedXmlDocInheritDoc -XmlDocument $xmlDocs
foreach ($memberName in @(
    'T:UmkaSharp.UmkaRuntime',
    'M:UmkaSharp.UmkaRuntime.FromFile(System.String,System.Int32,System.Boolean,System.Boolean,System.Collections.Generic.IReadOnlyList{System.String})',
    'M:UmkaSharp.UmkaRuntime.FromFile(System.String,UmkaSharp.UmkaRuntimeOptions)',
    'M:UmkaSharp.UmkaRuntime.FromSource(System.String,System.String,System.Int32,System.Boolean,System.Boolean,System.Collections.Generic.IReadOnlyList{System.String})',
    'M:UmkaSharp.UmkaRuntime.FromSource(System.String,UmkaSharp.UmkaRuntimeOptions)',
    'M:UmkaSharp.UmkaRuntime.FromSource(System.String,System.String,UmkaSharp.UmkaRuntimeOptions)',
    'M:UmkaSharp.UmkaRuntime.Compile',
    'M:UmkaSharp.UmkaRuntime.Run',
    'M:UmkaSharp.UmkaRuntime.GetFunction(System.String,System.String)',
    'M:UmkaSharp.UmkaRuntime.TryGetFunction(System.String,UmkaSharp.UmkaFunction@)',
    'M:UmkaSharp.UmkaRuntime.TryGetFunction(System.String,System.String,UmkaSharp.UmkaFunction@)',
    'M:UmkaSharp.UmkaRuntime.CreateHostHandle(System.Object)',
    'M:UmkaSharp.UmkaRuntime.GetHostObject``1(System.IntPtr)',
    'M:UmkaSharp.UmkaRuntime.TryGetHostObject``1(System.IntPtr,``0@)',
    'T:UmkaSharp.UmkaFunction',
    'P:UmkaSharp.UmkaFunction.Name',
    'P:UmkaSharp.UmkaFunction.ModuleName',
    'P:UmkaSharp.UmkaFunction.ParameterTypes',
    'P:UmkaSharp.UmkaFunction.ResultType',
    'M:UmkaSharp.UmkaFunction.CallValue(UmkaSharp.UmkaValue[])',
    'M:UmkaSharp.UmkaFunction.CallValue(System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'M:UmkaSharp.UmkaFunction.CallScalar``1(UmkaSharp.UmkaValue[])',
    'M:UmkaSharp.UmkaFunction.CallScalar``1(System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'M:UmkaSharp.UmkaFunction.CallEnum``1(UmkaSharp.UmkaValue[])',
    'M:UmkaSharp.UmkaFunction.CallEnum``1(System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'M:UmkaSharp.UmkaFunction.CallHostObject``1(UmkaSharp.UmkaValue[])',
    'M:UmkaSharp.UmkaFunction.CallHostObject``1(System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'M:UmkaSharp.UmkaFunction.CallStruct``1(System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'M:UmkaSharp.UmkaFunction.CallArray``1(System.Int32,System.ReadOnlySpan{UmkaSharp.UmkaValue})',
    'T:UmkaSharp.UmkaCallFrame',
    'P:UmkaSharp.UmkaCallFrame.ParameterTypes',
    'P:UmkaSharp.UmkaCallFrame.ResultType',
    'M:UmkaSharp.UmkaCallFrame.GetEnum``1(System.Int32)',
    'M:UmkaSharp.UmkaCallFrame.GetScalar``1(System.Int32)',
    'M:UmkaSharp.UmkaCallFrame.GetHostObject``1(System.Int32)',
    'M:UmkaSharp.UmkaCallFrame.TryGetHostObject``1(System.Int32,``0@)',
    'M:UmkaSharp.UmkaCallFrame.GetStruct``1(System.Int32)',
    'M:UmkaSharp.UmkaCallFrame.GetArray``1(System.Int32,System.Int32)',
    'T:UmkaSharp.UmkaHostHandle',
    'P:UmkaSharp.UmkaHostHandle.Address',
    'P:UmkaSharp.UmkaHostHandle.IsDisposed',
    'M:UmkaSharp.UmkaHostHandle.GetTarget``1',
    'M:UmkaSharp.UmkaHostHandle.ToValue',
    'T:UmkaSharp.UmkaValue',
    'P:UmkaSharp.UmkaValue.Kind',
    'P:UmkaSharp.UmkaValue.Value',
    'M:UmkaSharp.UmkaValue.FromScalar``1(``0)',
    'M:UmkaSharp.UmkaValue.FromEnum``1(``0)',
    'M:UmkaSharp.UmkaValue.FromHostHandle(UmkaSharp.UmkaHostHandle)',
    'M:UmkaSharp.UmkaValue.FromStruct``1(``0)',
    'M:UmkaSharp.UmkaValue.FromStaticArray``1(System.ReadOnlySpan{``0})',
    'M:UmkaSharp.UmkaValue.FromStaticArray``1(System.Span{``0})',
    'M:UmkaSharp.UmkaValue.AsEnum``1',
    'M:UmkaSharp.UmkaValue.AsStaticArray``1',
    'M:UmkaSharp.UmkaValue.AsStruct``1',
    'T:UmkaSharp.UmkaRuntimeOptions',
    'P:UmkaSharp.UmkaError.FileName',
    'P:UmkaSharp.UmkaError.FunctionName',
    'P:UmkaSharp.UmkaError.Line',
    'P:UmkaSharp.UmkaError.Position',
    'P:UmkaSharp.UmkaError.Code',
    'P:UmkaSharp.UmkaError.Message',
    'P:UmkaSharp.UmkaTypeInfo.HasReferences',
    'P:UmkaSharp.UmkaTypeInfo.ItemCount',
    'P:UmkaSharp.UmkaTypeInfo.Kind',
    'P:UmkaSharp.UmkaTypeInfo.NativeSize',
    'P:UmkaSharp.UmkaTypeInfo.TypeName',
    'P:UmkaSharp.UmkaRuntimeOptions.StackSize',
    'P:UmkaSharp.UmkaRuntimeOptions.FileSystemEnabled',
    'P:UmkaSharp.UmkaRuntimeOptions.ImplementationLibrariesEnabled',
    'P:UmkaSharp.UmkaRuntimeOptions.Arguments',
    'P:UmkaSharp.UmkaRuntimeOptions.WarningHandler')) {
    Assert-XmlDocMember -XmlDocument $xmlDocs -MemberName $memberName
}

$nuspecText = Get-ZipEntryText -Path $resolvedPackagePath -EntryName 'UmkaSharp.nuspec'
$nuspec = [xml]$nuspecText
$namespaceManager = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
$namespaceManager.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
$metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $namespaceManager)
if ($null -eq $metadata) {
    throw "Package '$resolvedPackagePath' does not contain nuspec metadata."
}

Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'id' -ExpectedValue $ExpectedPackageId
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'version' -ExpectedValue $ExpectedVersion
}

Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'title' -ExpectedValue 'UmkaSharp'
Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'authors' -ExpectedValue 'Andrea Mancuso'
Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'description' -ExpectedValue 'A .NET bridge for embedding the Umka programming language in C# applications.'
Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'readme' -ExpectedValue 'README.md'
Assert-NuspecValue -Metadata $metadata -NamespaceManager $namespaceManager -Name 'projectUrl' -ExpectedValue 'https://github.com/andreamancuso/Umka-CSharp'

$license = $metadata.SelectSingleNode('n:license', $namespaceManager)
if ($null -eq $license -or $license.GetAttribute('type') -ne 'expression' -or $license.InnerText.Trim() -ne 'MIT') {
    throw "Package metadata 'license' should be the MIT expression."
}

$repository = $metadata.SelectSingleNode('n:repository', $namespaceManager)
if ($null -eq $repository -or $repository.GetAttribute('type') -ne 'git' -or $repository.GetAttribute('url') -ne 'https://github.com/andreamancuso/Umka-CSharp.git') {
    throw "Package metadata 'repository' should point to the Umka-CSharp git repository."
}

$repositoryCommit = $repository.GetAttribute('commit')
if ($repositoryCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Package metadata 'repository' should include a 40-character git commit for Source Link traceability."
}

$dependencyGroup = $metadata.SelectSingleNode('n:dependencies/n:group[@targetFramework="net9.0"]', $namespaceManager)
if ($null -eq $dependencyGroup) {
    throw "Package metadata should contain a net9.0 dependency group."
}

Assert-NuspecDependencyGroupIsEmpty -DependencyGroup $dependencyGroup -NamespaceManager $namespaceManager

foreach ($tag in @('umka', 'scripting', 'interop', 'embedding', 'language-bridge')) {
    Assert-NuspecContainsTag -Metadata $metadata -NamespaceManager $namespaceManager -Tag $tag
}

if ($RequireSymbols) {
    $symbolPackagePath = [System.IO.Path]::ChangeExtension($resolvedPackagePath, '.snupkg')
    if (-not (Test-Path -LiteralPath $symbolPackagePath)) {
        throw "Cannot find symbol package '$symbolPackagePath'."
    }

    $symbolEntries = Get-ZipEntries -Path $symbolPackagePath
    Assert-SymbolPackageEntriesAreAllowed -Entries $symbolEntries -ArchivePath $symbolPackagePath
    Assert-ZipEntry -Entries $symbolEntries -EntryName 'UmkaSharp.nuspec' -ArchivePath $symbolPackagePath
    Assert-ZipEntry -Entries $symbolEntries -EntryName 'lib/net9.0/UmkaSharp.pdb' -ArchivePath $symbolPackagePath
    Assert-ZipEntryHasContent -ArchivePath $symbolPackagePath -EntryName 'UmkaSharp.nuspec'
    Assert-ZipEntryHasContent -ArchivePath $symbolPackagePath -EntryName 'lib/net9.0/UmkaSharp.pdb'
    Assert-SymbolPdbContainsSourceLink `
        -ArchivePath $symbolPackagePath `
        -EntryName 'lib/net9.0/UmkaSharp.pdb' `
        -RepositoryCommit $repositoryCommit
}
