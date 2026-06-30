# Examples

Runnable examples live under `samples/`.

| Sample | Purpose |
| --- | --- |
| `Smoke` | Minimal end-to-end runtime, module, callback, and function call. |
| `Calculator` | Plain C# to Umka function calls with numeric arguments and results. |
| `HostCallbacks` | Umka calling C# callbacks that return integers and strings. |
| `Modules` | Registering an importable source-string module before compilation. |
| `StructuredData` | Reading a static-array result into a managed sequential struct. |
| `PackageConsumer` | Consumes a locally packed NuGet package and exercises representative runtime, callback, host-handle, module, argument, marshalling, error, warning, and file-system behavior. |

Run the project-reference samples listed in `samples/project-reference-samples.txt` in Release mode after building the native shim:

```powershell
.\native\build_windows_msvc.ps1 -Configuration Release
.\scripts\verify-samples.ps1 -Configuration Release
```

The verifier runs each project-reference sample and checks the output lines listed in that sample's `expected-output.txt` file.
CI runs the same verifier on Windows and Ubuntu after building and testing the solution.

The `PackageConsumer` project is not included in `UmkaSharp.sln` because normal source builds should not depend on a locally packed NuGet package. It can be checked with `scripts/verify-package-local.ps1` when packaging behavior changes; the script copies the sample to `artifacts/package-smoke`, pins it to the requested package version, restores from the produced `.nupkg`, runs it, and checks the output lines listed in `samples/PackageConsumer/expected-output.txt`.
