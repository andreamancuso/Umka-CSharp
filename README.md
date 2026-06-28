# UmkaSharp

A .NET bridge for embedding the [Umka](https://github.com/vtereshkov/umka-lang) language in C# applications.

This repository is being prepared for a NuGet package named `UmkaSharp`.

The repository is pinned to .NET SDK 9 via `global.json` and targets `net9.0`.

## Current Shape

- `src/UmkaSharp`: managed runtime, function, callback, value, and error APIs.
- `native`: `umka_shim`, a small C ABI that embeds Umka and protects the managed boundary.
- `tests/UmkaSharp.Tests`: focused smoke tests for startup, calls, callbacks, and errors.
- `samples/Smoke`: a tiny app that calls Umka and a C# callback.

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

Verify the Linux native asset with Docker:

```powershell
.\scripts\verify-linux-docker.ps1
```

Create a local NuGet package:

```powershell
.\scripts\pack-local.ps1 -Version 0.1.0-local
```

Run the Docker verifier before packing when the local package should include `runtimes/linux-x64/native/libumka_shim.so`.

See [docs/publishing.md](docs/publishing.md) for the NuGet release checklist.

## Example

```csharp
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

var answer = runtime.GetFunction("answer").CallInt64();
Console.WriteLine(answer); // 42
```
