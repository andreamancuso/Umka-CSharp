# Troubleshooting

## `DllNotFoundException` For `umka_shim`

Build the native shim before running tests or samples from source:

```powershell
.\native\build_windows_msvc.ps1 -Configuration Release
```

For Linux verification from Windows, use Docker:

```powershell
.\scripts\verify-linux-docker.ps1
```

When consuming the NuGet package, make sure the package includes the native asset for your RID under `runtimes/{rid}/native/`.

## Package Does Not Contain Linux Assets

Run the Linux Docker verifier before packing locally:

```powershell
.\scripts\verify-linux-docker.ps1
.\scripts\pack-local.ps1 -Version 0.1.0-local -Configuration Release
```

`pack-local.ps1` copies an existing Linux shim into `runtimes/linux-x64/native/` when it is present.

## Function Not Found

Only exported Umka functions can be resolved from C#. Mark the function with `*` in Umka source:

```umka
fn answer*(): int {
    return 42
}
```

Call `Compile()` before `GetFunction()`.

## Wrong Argument Count

Check `UmkaFunction.ParameterCount`. Calls must pass exactly that many `UmkaValue` arguments.

## Runtime Used From Another Thread

Use the runtime on the thread that created it. For parallel work, create one runtime per worker thread.

## Callback Throws

Inspect the returned `UmkaCallback.LastException`. The Umka call should throw `UmkaException` after the managed callback fails.

## Local Package Smoke

After packing, verify the package in a clean console project:

```powershell
.\scripts\verify-package-local.ps1 -Version 0.1.0-local -Configuration Release
```

By default the local package verifier requires the current platform RID and any supported `win-x64` or `linux-x64` assets already present under `runtimes/`. Pass `-RequiredRuntimeIdentifiers win-x64,linux-x64` when you want an explicit pre-release layout check for both supported RIDs.
