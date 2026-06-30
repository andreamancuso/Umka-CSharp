# Limitations

UmkaSharp is an alpha embedding layer. It is useful for hosted scripts, exported function calls, modules, scalar marshalling, fixed-layout structured values, managed callbacks, and runtime-owned host handles. The API is not stable.

## Supported Today

- .NET 9
- `win-x64` and `linux-x64` native assets
- source-string runtime creation
- one-shot source-string execution through `RunSource()` and `TryRunSource()`
- pre-compile source modules
- exported function lookup
- sandboxed native file-system access by default, with opt-in file-system access through `UmkaRuntimeOptions.FileSystemEnabled`
- C# to Umka scalar arguments
- Umka enum values as their underlying signed or unsigned integer values
- Umka enum member metadata through `UmkaTypeInfo.IsEnum` and `UmkaTypeInfo.EnumMembers`
- omitted trailing Umka default parameters for scalar, string, and pointer defaults
- C# to Umka fixed-layout struct and static-array arguments without Umka-managed reference fields, including weak pointer fields or elements as `ulong` opaque handles
- C# to Umka dynamic-array arguments without Umka-managed reference elements, including `[]weak ^T` values as `ulong[]` opaque handle arrays
- C# to Umka `[]str` dynamic-array arguments through `UmkaValue.FromDynamicArray(string?[])`
- C# to Umka nested dynamic-array arguments such as `[][]int` and `[][]str` through `UmkaValue.FromNestedDynamicArray<TElement>()` and `UmkaValue.FromNestedDynamicArray(string?[][])` when inner element values do not contain Umka-managed references or are direct `str`
- scalar, string, pointer, and structured Umka results, including fixed-layout weak pointer fields or elements as `ulong` opaque handles
- opaque Umka weak pointer handles as function arguments, function results, callback arguments, and callback results
- dynamic-array Umka results copied into managed arrays when element values do not contain Umka-managed references, including `[]weak ^T` values as `ulong[]` opaque handle arrays
- `[]str` Umka results copied into managed string arrays through `CallStringArray()` and `TryCallStringArray()`
- nested dynamic-array Umka results such as `[][]int` and `[][]str` copied into managed jagged arrays through `CallNestedDynamicArray<TElement>()`, `TryCallNestedDynamicArray<TElement>()`, `CallNestedStringArray()`, and `TryCallNestedStringArray()` when inner element values do not contain Umka-managed references or are direct `str`
- map Umka results copied into managed dictionaries when key/value types are fixed-layout values without Umka-managed references, direct `str` values copied through string-specific map APIs, dynamic-array values have reference-free or direct-`str` elements, or weak pointer handles copied as `ulong`
- fixed-layout multiple-return values read as managed sequential structs
- Umka programs that use fibers internally, when invoked through `Run()` or exported function calls
- Umka to C# callbacks for scalar/string/pointer values, fixed-layout aggregate arguments/results, `[]str` dynamic-array arguments/results, and dynamic-array arguments/results without Umka-managed reference elements, including fixed-layout weak pointer fields/elements as `ulong` opaque handles and `[]weak ^T` values as `ulong[]` opaque handle arrays
- Umka to C# callback nested dynamic-array arguments such as `[][]int` and `[][]str` copied into managed jagged arrays through `GetNestedDynamicArray<TElement>()`, `TryGetNestedDynamicArray<TElement>()`, `GetNestedStringArray()`, and `TryGetNestedStringArray()` when inner element values do not contain Umka-managed references or are direct `str`
- C# callback nested dynamic-array results such as `[][]int` and `[][]str` through `UmkaValue.FromNestedDynamicArray<TElement>()` and `UmkaValue.FromNestedDynamicArray(string?[][])` under the same inner-element rules
- Umka to C# callback map arguments copied into managed dictionaries when key/value types are fixed-layout values without Umka-managed references, direct `str` values copied through string-specific map APIs, dynamic-array values have reference-free or direct-`str` elements, or weak pointer handles copied as `ulong`
- runtime-owned managed host handles passed through Umka pointer values
- deterministic runtime disposal
- thread-affinity checks

## Deferred Or Metadata-Only Matrix

| Area | Public status | Reason |
| --- | --- | --- |
| First-class map wrappers, C# map arguments, and C# callback map results | Rejected or absent from the public API | Umka's public C API exposes `umkaGetMapItem()` and map key/value metadata, but map allocation, insertion, assignment, reference-count updates, rooting, and ownership transfer are implemented inside VM helpers used by language builtins. |
| Interface and `any` values as managed objects | Metadata-only | Umka represents `any` as an `Interface`/`UmkaAny` value with `#self` and `#selftype`, while non-empty interfaces append method entry offsets. Safe managed values need self-value rooting, self-type assertions, method-table retention, runtime lifetime, thread-affinity, and call-error behavior. |
| Closure values as C# callable or retained handles | Metadata-only, except registered C# callbacks | Arbitrary Umka closures contain an entry offset plus captured `any` upvalue state. Safe support needs upvalue rooting, function-context construction, runtime lifetime, thread-affinity, reentrancy, and nested-call failure rules. |
| Fiber values as host-created or host-resumed objects | Metadata-only | Umka stores a fiber value as an internal `Fiber *`. Creation, resume, and validity checks are language builtins, and the public C API does not expose arbitrary host fiber creation, resume, alive-status inspection, rooting, or ownership transfer. |
| Long-lived rooted Umka heap wrappers | Absent from the public API | Supported values are copied across the boundary. A wrapper model needs per-type runtime ownership, thread-affinity, native reference-count/rooting, disposal/finalization, callback-reentrancy, and runtime-shutdown rules. |
| Umka-owned managed object wrappers through `umkaAllocData(size, onFree)` | Deferred | Current host handles are managed-runtime-owned `GCHandle` wrappers. Umka-owned wrappers need native finalizer ordering, managed `GCHandle` release, thread-affinity, runtime shutdown, callback reentrancy, and cleanup error behavior. |
| Nested dynamic arrays whose inner element type contains references beyond direct `str` | Rejected | Copying `[][]any`, `[][]map[...]T`, or nested arrays of aggregates containing maps, interfaces, closures, fibers, or `any` would copy Umka heap references without a managed ownership model. |
| Dynamic arrays whose element type contains references beyond direct `str` or supported nested arrays | Rejected | Arrays of maps, interfaces, closures, fibers, `any`, or aggregates containing those values cannot be copied safely without rooting, retaining, and release rules for each entry. |
| Map key/value shapes beyond direct `str`, reference-free values, supported `[]T`, or supported `[]str` values | Rejected | Maps of interfaces, closures, fibers, `any`, maps, or aggregates containing those values would duplicate Umka heap references without ownership, rooting, retaining, or release behavior. |
| Aggregate arguments, function results, or callback results containing Umka-managed references | Rejected | UmkaSharp copies aggregate storage by value and does not own, root, retain, release, or invoke references embedded in strings, dynamic arrays, maps, interfaces, closures, fibers, or `any` fields. |
| Omitted defaults for heap-backed parameters | Rejected | Omitted defaults for dynamic arrays, maps, interfaces, closures, fibers, `any`, or reference-bearing aggregates require heap/reference ownership rules that UmkaSharp does not expose. |
| Additional package RIDs such as `linux-arm64`, `osx-x64`, and `osx-arm64` | Candidate-only | A RID is listed as package-supported only after a native asset is built, package layout verification covers it, and a package-consumer smoke test passes on that platform. |

See [platforms.md](platforms.md) for the supported RID list and the promotion checklist for candidate platforms.

## Current Umka Source Constraints

- weak pointer default parameters cannot currently be declared in Umka source. Umka rejects `null` and weak pointer casts in default expressions because conversion to a weak pointer is not allowed in constant expressions. Pass weak pointer arguments explicitly with `UmkaValue.FromWeakPointer(handle)`.
- host-side map creation is not exposed by Umka's public C API. The API provides map lookup through `umkaGetMapItem()` and key/value metadata through `umkaGetMapKeyType()` and `umkaGetMapItemType()`, but not map allocation, insertion, root ownership, ownership transfer, or reference-count assignment operations. UmkaSharp can copy readable map results and callback arguments, but it rejects C# map arguments, C# callback map results, and long-lived map wrappers.
- `any` and declared interface values share Umka's `TYPE_INTERFACE` representation. The public C header defines the empty shape as `UmkaAny`, and Umka's common header notes that methods are omitted from that C equivalent and `sizeof()` must not be used for non-empty interfaces. UmkaSharp therefore treats interface values as metadata-only until a native/managed wrapper can preserve the self pointer, self type, method entries, rooting, casting, runtime lifetime, thread-affinity, and call error behavior.
- closure values are represented by Umka as an entry offset plus an `any` upvalue. UmkaSharp can register C# callbacks before compilation through Umka's closure registration path, but it does not expose arbitrary Umka closure values as callable or retained managed objects because that would require copying/rooting captured upvalue state, building a function context from the closure, and defining runtime lifetime, thread-affinity, reentrancy, and nested-call failure rules.
- fiber values are represented by Umka as an internal `Fiber *`. The `Fiber` struct carries VM execution state including stack pointers, registers, parent fiber, instruction pointer, VM pointer, alive state, and debug metadata. Creating and switching fibers is implemented by the Umka `make(fiber, closure)` and `resume` builtins, not by exported C API functions, so UmkaSharp exposes fiber values only as metadata until a native host API and managed ownership model exist.

## Deliberately Not Exposed

- expression-only eval: Umka's native compilation boundary is a complete source unit with declarations, imports, prototypes, and explicit result types. Use `RunSource()` / `TryRunSource()` for one-shot programs, or wrap expressions in exported typed functions and call them through `CompileSource()` plus `GetFunction()`.

## Design Status

The API is intentionally conservative. Features should be added only when ownership, lifetime, type conversion, failure behavior, tests, and native package implications are clear.
