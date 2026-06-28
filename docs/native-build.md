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

On Windows with Visual Studio 2022 but no CMake on `PATH`, use:

```powershell
.\native\build_windows_msvc.ps1 -Configuration Release
```

To verify the Linux native library from Windows, run Docker Desktop with Linux containers and use:

```powershell
.\scripts\verify-linux-docker.ps1
```

That script mounts this repository and `C:\dev\umka-lang` into a .NET 9 SDK Linux container, builds `libumka_shim.so`, runs the .NET test suite inside Linux, and copies the Linux NuGet runtime asset to `runtimes/linux-x64/native/`.

The .NET project copies `native/build/Release/umka_shim.dll`, `native/build/libumka_shim.so`, or `native/build/libumka_shim.dylib` to the test output when present. CI is responsible for assembling NuGet assets under `runtimes/{rid}/native/`.
