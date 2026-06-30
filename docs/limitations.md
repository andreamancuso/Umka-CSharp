# Limitations

UmkaSharp is an alpha embedding layer. It is useful for hosted scripts, exported function calls, modules, scalar marshalling, fixed-layout structured values, managed callbacks, and runtime-owned host handles. The API is not stable.

## Supported Today

- .NET 9
- `win-x64` and `linux-x64` native assets
- source-string runtime creation
- pre-compile source modules
- exported function lookup
- sandboxed native file-system access by default, with opt-in file-system access through `UmkaRuntimeOptions.FileSystemEnabled`
- C# to Umka scalar arguments
- Umka enum values as their underlying signed or unsigned integer values
- C# to Umka fixed-layout struct and static-array arguments without Umka-managed reference fields
- scalar, string, pointer, and structured Umka results
- fixed-layout multiple-return values read as managed sequential structs
- Umka programs that use fibers internally, when invoked through `Run()` or exported function calls
- Umka to C# callbacks for scalar/string/pointer values and fixed-layout aggregate arguments/results without Umka-managed reference fields
- runtime-owned managed host handles passed through Umka pointer values
- deterministic runtime disposal
- thread-affinity checks

## Not Yet Supported As Managed Abstractions

- ad hoc `Eval` helpers
- dynamic arrays
- maps
- interfaces
- closures
- weak pointers
- managed host-side fiber wrappers for creating, resuming, or inspecting Umka fibers
- `any`, which appears as interface metadata at the native boundary
- managed result readers for dynamic arrays, maps, interfaces, closures, weak pointers, fibers, or `any`
- long-lived rooted Umka heap values
- Umka-side lifetime callbacks for managed host handles; Umka's native `umkaAllocData(size, onFree)` path exists, but UmkaSharp host handles are managed-runtime-owned `GCHandle` wrappers rather than Umka-owned heap objects
- dedicated enum metadata/member-name wrappers
- aggregate arguments that contain Umka-managed references
- aggregate function results that contain Umka-managed references
- aggregate callback results that contain Umka-managed references
- omitted trailing arguments for Umka default parameters in C# calls; pass all exported parameters explicitly
- expanded C# calls to Umka variadic parameter lists, which are exposed as unsupported dynamic-array parameters
- additional package RIDs such as `linux-arm64`, `osx-x64`, and `osx-arm64`

See [platforms.md](platforms.md) for the supported RID list and the promotion checklist for candidate platforms.

## Design Status

The API is intentionally conservative. Features should be added only when ownership, lifetime, type conversion, failure behavior, tests, and native package implications are clear.
