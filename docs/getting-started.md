# Getting Started

UmkaSharp embeds Umka in a .NET 9 process through a small native shim. The managed package exposes `UmkaRuntime`, `UmkaFunction`, `UmkaValue`, `UmkaCallback`, and `UmkaException`.

## Install

For local development before a NuGet release, build a package and consume it from `artifacts/packages`:

```powershell
.\scripts\pack-local.ps1 -Version 0.1.0-local -Configuration Release
dotnet add package UmkaSharp --version 0.1.0-local --source .\artifacts\packages
```

Alpha releases should be installed from NuGet.org with the published pre-release version:

```powershell
dotnet add package UmkaSharp --version 0.1.0-alpha.1
```

Once a stable package exists, `dotnet add package UmkaSharp` will use the latest stable release.

## Minimal Runtime

```csharp
using UmkaSharp;

using var runtime = UmkaRuntime.CompileSource("""
    fn answer*(): int {
        return 42
    }
    """);

var answer = runtime.GetFunction("answer");
Console.WriteLine(answer.CallInt64());
```

Exported Umka functions use `*` in the source. `CompileSource()` creates and compiles the runtime in one step. Pass `configure: runtime => { ... }` to `CompileSource()` or `CompileFile()` when you need to add modules or register callbacks before compilation. Use `TryCompileSource()` or `TryCompileFile()` when compile errors are expected user input and should be handled as `UmkaError` data without catching `UmkaException`.

Use `Run()` when the script has a `main()` entry point:

```csharp
using var runtime = UmkaRuntime.CompileSource("""
    fn main() {
    }
    """);

runtime.Run();
```

Use `TryRun(out exception)` when `main()` runtime errors are expected input and should be handled without catching `UmkaException`.

For one-shot scripts where the host does not need to retain exported functions, use `RunSource()`:

```csharp
UmkaRuntime.RunSource("""
    fn main() {
    }
    """);
```

Use `TryRunSource(out exception)` when compile or runtime errors from a complete source string should be returned as data. Both helpers dispose the transient runtime before returning or throwing.

## Loading From Files

Use `CompileFile` when the main Umka program lives on disk:

```csharp
using var runtime = UmkaRuntime.CompileFile("main.um");
runtime.GetFunction("mainHook").CallVoid();
```

Sibling imports are resolved by Umka from the importing source file path. Pass `configure:` when the host needs to register callbacks or in-memory modules before compilation.

When the main source is in memory but an importable module lives on disk, register it before compilation:

```csharp
using var runtime = UmkaRuntime.CompileSource(
    """
    import "math.um"

    fn answer*(): int {
        return math::inc(41)
    }
    """,
    configure: configured =>
        configured.AddModuleFromFile("math.um", @"C:\scripts\math.um"));
```

## Command Arguments

Pass host-defined command-line arguments with `UmkaRuntimeOptions`:

```csharp
using var runtime = UmkaRuntime.CompileSource(source, new UmkaRuntimeOptions
{
    Arguments = ["script.um", "alpha"],
});
```

Umka code can read them through `std::argc()` and `std::argv(index)`. UmkaSharp does not add a program name automatically; include one as the first item when the script expects CLI-style indexing.

`UmkaRuntimeOptions` also exposes `StackSize`, `FileSystemEnabled`, `ImplementationLibrariesEnabled`, and `WarningHandler` for hosts that need to configure the native Umka runtime at startup or capture compile warnings. `FileSystemEnabled` defaults to `false`; set it to `true` only when the script should be allowed to use `std.um` file, environment, or system helpers against the host operating system.

## Compile Errors As Data

```csharp
if (!UmkaRuntime.TryCompileSource(source, out var runtime, out var error))
{
    Console.Error.WriteLine(error.Message);
    return;
}

using (runtime)
{
    Console.WriteLine(runtime.GetFunction("answer").CallInt64());
}
```

Try-style compiled factories return `false` only for native Umka compile errors. Invalid host arguments, configuration callback failures, warning-handler failures, and native initialization failures still throw. When compilation returns `false`, UmkaSharp has already disposed the transient runtime.

## Calling With Arguments

```csharp
using var runtime = UmkaRuntime.CompileSource("""
    fn add*(a, b: int): int {
        return a + b
    }
    """);

var add = runtime.GetFunction("add");

Console.WriteLine(add.ParameterCount);
Console.WriteLine(add.CallInt64(UmkaValue.From(19), UmkaValue.From(23)));
```

`ParameterCount` is the total number of explicit Umka parameters. `RequiredParameterCount` is lower when the function has trailing default parameters that UmkaSharp can synthesize safely, or when the final Umka parameter is variadic. C# calls may omit trailing scalar, string, and pointer defaults; extra arguments, missing required arguments, and omitted defaults for unsupported heap-backed types are rejected before native parameter slots are written. Current Umka source rejects weak pointer default expressions, so weak pointer arguments must be passed explicitly. Umka variadic parameters are exposed as dynamic-array metadata. Pass either one explicit `UmkaValue.FromDynamicArray<TElement>(...)` value for the variadic parameter, or pass expanded trailing `UmkaValue` arguments whose kinds and ranges match the variadic element type.

## Next Steps

- [API concepts](api-concepts.md)
- [Marshalling](marshalling.md)
- [Callbacks](callbacks.md)
- [Errors](errors.md)
- [Threading and lifetime](threading-lifetime.md)
- [Troubleshooting](troubleshooting.md)
