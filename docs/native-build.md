# Native Build

UmkaSharp builds a native `umka_shim` library from the Umka C sources plus `native/umka_shim.c`.

Local development uses the checkout at:

```text
C:\dev\umka-lang
```

For portable builds, set `UMKA_ROOT` to any Umka checkout that contains `src/umka_api.h`.

```powershell
$env:UMKA_ROOT = 'C:\dev\umka-lang'
cmake -S native -B native/build -DCMAKE_BUILD_TYPE=Release
cmake --build native/build --config Release
```

CI does not build against a moving Umka branch. `.github/workflows/ci.yml` pins `vtereshkov/umka-lang` through `UMKA_REF`:

```text
a66d5bf830130dd5435b4661ec15bfd34c11a08f
```

When changing the Umka baseline, first update and test the local `C:\dev\umka-lang` checkout, then update `UMKA_REF` in the workflow to the verified commit.

On Windows with Visual Studio 2022 but no CMake on `PATH`, use:

```powershell
.\native\build_windows_msvc.ps1 -Configuration Release
```

To verify the Linux native library from Windows, run Docker Desktop with Linux containers and use:

```powershell
.\scripts\verify-linux-docker.ps1
```

That script first checks that `THIRD-PARTY-NOTICES.md` includes the full license text from the selected Umka checkout, then mounts this repository and `C:\dev\umka-lang` into a .NET 9 SDK Linux container, builds `libumka_shim.so`, runs the .NET test suite inside Linux, and copies the Linux NuGet runtime asset to `runtimes/linux-x64/native/`.

The .NET project copies `native/build/Release/umka_shim.dll`, `native/build/libumka_shim.so`, or `native/build/libumka_shim.dylib` to the test output when present. CI is responsible for assembling NuGet assets under `runtimes/{rid}/native/`.

See [platforms.md](platforms.md) for the supported RID list and the checklist for adding future native assets.
