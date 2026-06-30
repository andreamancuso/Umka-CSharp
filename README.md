# UmkaSharp

A .NET bridge for embedding the [Umka](https://github.com/vtereshkov/umka-lang) language in C# applications.

UmkaSharp is an alpha NuGet package for hosts that want to compile Umka source, call exported Umka functions, expose C# callbacks to Umka, and exchange supported scalar and fixed-layout values through a conservative managed API.

The repository is pinned to .NET SDK 9 via `global.json` and targets `net9.0`. NuGet publishing is intentionally limited to GitHub releases; ordinary pushes, pull requests, tags, and manual workflow runs build and test packages but do not publish them.

## Package Facts

- Target framework: `net9.0`
- Supported package RIDs: `win-x64` and `linux-x64`
- Native bridge: `umka_shim`, a small C ABI built from this repository and the Umka C sources
- Public API areas: runtime lifecycle, source/module loading, compilation, exported function lookup, typed calls, callbacks, host handles, errors, disposal, and fixed-layout marshalling
- Verification: native-backed tests, sample output checks, package-consumer smoke tests, package layout checks, and release-only publishing policy checks

UmkaSharp is an alpha package. The supported API surface and known limitations are documented below.

## Install

For local package testing, build a package and consume it from `artifacts/packages`:

```powershell
.\scripts\pack-local.ps1 -Version 0.1.0-local -Configuration Release
dotnet add package UmkaSharp --version 0.1.0-local --source .\artifacts\packages
```

After a GitHub alpha release publishes the package, use NuGet.org with the published pre-release version:

```powershell
dotnet add package UmkaSharp --version 0.1.0-alpha.1
```

Once a stable package exists, `dotnet add package UmkaSharp` will use the latest stable release.

Packages include RID-specific native assets under `runtimes/{rid}/native/`. See [docs/platforms.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/platforms.md) before relying on another platform.

## Quick Start

```csharp
using UmkaSharp;

using var runtime = UmkaRuntime.CompileSource("""
    fn answer*(): int {
        return 42
    }
    """);

var answer = runtime.GetFunction("answer").CallInt64();
Console.WriteLine(answer); // 42
```

Exported Umka functions use `*` in the source. `CompileSource()` and `CompileFile()` are the shortest path for ordinary hosts. Pass `configure: runtime => { ... }` when modules or callbacks need to be registered before compilation. Use `TryCompileSource()` or `TryCompileFile()` when compile errors are expected user input and should be handled as `UmkaError` data without catching `UmkaException`; use `TryRun(out exception)` when `main()` runtime errors should be handled the same way while preserving callback inner exceptions.

## Host Callbacks

```csharp
using var runtime = UmkaRuntime.CompileSource(
    """
    import "host.um"

    fn answer*(): int {
        return host::doubleIt(21)
    }
    """,
    configure: configured =>
    {
        configured.AddModule("host.um", "fn doubleIt*(x: int): int");
        configured.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
    });

Console.WriteLine(runtime.GetFunction("answer").CallInt64()); // 42
```

Callback exceptions are captured as managed failures and terminate the Umka runtime in the same way as other runtime errors. See [docs/callbacks.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/callbacks.md) and [docs/errors.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/errors.md) for the exact failure contract.

## Marshalling

The first public marshalling layer is explicit and copy-based. Supported values include:

- signed and unsigned integers, including checked narrow helpers
- `real`, `real32`, `bool`, `char`, and `str`
- raw pointers and runtime-owned host handles
- fixed-layout structs and static arrays through strict and try-style value factories, value readers, function readers, and callback readers when managed and native sizes match
- generic scalar helpers through `FromScalar<T>()`, `TryFromScalar<T>()`, `AsScalar<T>()`, `TryAsScalar<T>()`, `CallScalar<T>()`, `TryCallScalar<T>()`, `GetScalar<T>()`, and `TryGetScalar<T>()`
- dynamic scalar/string/pointer/void results through `CallValue()` and `TryCallValue()`

Dynamic arrays, maps, interfaces, closures, fibers, weak pointers, `any`, and reference-bearing aggregates are exposed only as metadata or rejected until safe ownership rules exist.

## Local Native Build

The native shim builds against a local Umka checkout. On this machine the source is available at:

```text
C:\dev\umka-lang
```

Set `UMKA_ROOT` when building elsewhere.

```powershell
$env:UMKA_ROOT = 'C:\dev\umka-lang'
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
dotnet test
```

If CMake is not available on Windows, use:

```powershell
.\native\build_windows_msvc.ps1 -Configuration Release
dotnet test
```

When you need to refresh or verify the Linux native asset locally, use Docker:

```powershell
.\scripts\verify-linux-docker.ps1
```

See [docs/native-build.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/native-build.md) for compiler requirements, `UMKA_ROOT`, CMake, and Docker details.

Create a local NuGet package:

```powershell
.\scripts\pack-local.ps1 -Version 0.1.0-local
```

Run the Docker verifier before packing only when the local package should include a freshly built `runtimes/linux-x64/native/libumka_shim.so`.

## Verification

Use focused checks while developing:

```powershell
dotnet test UmkaSharp.sln --configuration Release --verbosity minimal
.\scripts\verify-samples.ps1 -Configuration Release
.\scripts\verify-docs.ps1
.\scripts\verify-package-local.ps1 -Version 0.1.0-local -Configuration Release
```

Use the broader release-preparation checks in [docs/publishing.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/publishing.md) when native packaging, package metadata, or release workflow behavior changes.

## Repository Layout

- `src/UmkaSharp`: managed runtime, function, callback, value, host-handle, type-metadata, and error APIs
- `native`: CMake/native build files and the `umka_shim` C ABI
- `tests/UmkaSharp.Tests`: native-backed lifecycle, module, callback, marshalling, error, stress, package-surface, and public-doc tests
- `samples`: project-reference samples plus a NuGet package-consumer sample
- `docs`: API, marshalling, callbacks, errors, lifetime, platform, publishing, examples, troubleshooting, and limitations docs

See [docs/publishing.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/publishing.md) for the NuGet release checklist.

See [docs/platforms.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/platforms.md) for supported package RIDs and the platform expansion checklist.

See [docs/getting-started.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/getting-started.md) for installation and first-call usage.

See [docs/api-concepts.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/api-concepts.md) for the runtime, module, function, and value model.

See [docs/examples.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/examples.md) for the runnable sample projects.

See [docs/marshalling.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/marshalling.md) for supported C# and Umka value conversions.

See [docs/callbacks.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/callbacks.md), [docs/errors.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/errors.md), [docs/threading-lifetime.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/threading-lifetime.md), [docs/limitations.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/limitations.md), and [docs/troubleshooting.md](https://github.com/andreamancuso/Umka-CSharp/blob/main/docs/troubleshooting.md) for focused runtime guidance.
