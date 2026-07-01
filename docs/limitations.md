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
- C# to Umka map arguments through `UmkaValue.FromMap<TKey, TValue>()`, `FromStringKeyMap<TValue>()`, `FromStringValueMap<TKey>()`, and `FromStringMap()` when key/value types are fixed-layout values without Umka-managed references or direct `str`
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
- C# callback map results through the same `UmkaValue.FromMap...` factories as function arguments, under the same key/value rules
- Umka to C# callback map arguments copied into managed dictionaries when key/value types are fixed-layout values without Umka-managed references, direct `str` values copied through string-specific map APIs, dynamic-array values have reference-free or direct-`str` elements, or weak pointer handles copied as `ulong`
- built-in `any` results and callback arguments inspected through `CallAny()`, `TryCallAny(...)`, `GetAny(index)`, and `TryGetAny(...)`
- built-in `any` arguments and callback results constructed through `UmkaAnyValue.Null`, scalar/string `UmkaAnyValue.From(...)`, and retained same-runtime native payloads through `UmkaAnyValue.From(UmkaNativeValue)`
- retained concrete Umka structs assigned to non-empty interface parameters or callback results when Umka can construct the native method table
- retained direct `fn` and closure values invoked from C# through `UmkaNativeValue.AsCallable()` or `TryAsCallable(out function)` while the owning runtime and retained value remain alive
- runtime-owned managed host handles passed through Umka pointer values
- retained native values through `UmkaNativeValue` for supported Umka heap values passed back unchanged to the same runtime and equivalent Umka type
- deterministic runtime disposal
- thread-affinity checks
- fork-backed native interruption through `RequestInterrupt`, `ClearInterrupt`, and `IsInterruptRequested`

## Deferred Or Metadata-Only Matrix

| Area | Public status | Reason |
| --- | --- | --- |
| First-class map wrappers and unsupported map payload shapes | Partly supported | `UmkaNativeValue` can retain a supported map and pass it back unchanged to the same runtime and equivalent map type. UmkaSharp does not expose map entries, mutation, or unsupported reference-bearing map payloads through managed wrappers. |
| Built-in `any` values | Supported with explicit payload limits | `UmkaAnyValue` inspects/deconstructs `any`, constructs null/scalar/string payloads, and boxes retained same-runtime `UmkaNativeValue` payloads. Managed aggregate snapshots are rejected because they do not carry the concrete native Umka type metadata needed to allocate an `any` payload. |
| Declared non-empty interfaces | Partly supported | `UmkaNativeValue` can retain exact interface values, and retained concrete Umka structs can be assigned to non-empty interface parameters or callback results through the fork-backed `umkaMakeInterface` path. UmkaSharp does not expose managed method-table inspection, host construction from managed data, runtime type assertions, or interface method dispatch from C#. |
| Closure and direct `fn` values as C# callable objects | Supported with retained-value lifetime | `UmkaNativeValue.AsCallable()` builds a managed `UmkaFunction` over the retained callable and uses the fork-backed callable context APIs. Invocation remains thread-affine, terminates the runtime on Umka runtime errors, observes pending interruption, and requires the retained value to stay undisposed. |
| Fiber values as host-created or host-resumed objects | Metadata-only | Umka stores a fiber value as an internal `Fiber *`. Creation, resume, and validity checks are language builtins, and the public C API does not expose arbitrary host fiber creation, resume, alive-status inspection, rooting, or ownership transfer. |
| Long-lived rooted Umka heap wrappers | Opaque retain-only | `UmkaNativeValue` owns retained native values, but it is intentionally opaque. A full wrapper model still needs per-type inspection/mutation/invocation APIs, runtime ownership, thread-affinity, native reference-count/rooting, disposal/finalization, callback-reentrancy, and runtime-shutdown rules. |
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
- host-side map creation in UmkaSharp relies on fork-backed native shim APIs for map allocation and insertion. UmkaSharp can copy supported map arguments, callback map results, readable map results, and callback map arguments. It can also retain supported maps as opaque native values, but it still rejects map entry wrappers and maps whose entries require unsupported heap ownership, rooting, retaining, or release behavior.
- `any` and declared interface values share Umka's `TYPE_INTERFACE` representation. The public C header defines the empty shape as `UmkaAny`, and Umka's common header notes that methods are omitted from that C equivalent and `sizeof()` must not be used for non-empty interfaces. UmkaSharp supports built-in `any` payload inspection and construction through the fork-backed host-value APIs, and can ask Umka to construct a non-empty interface value from a retained concrete Umka struct. Managed method-table inspection, host construction from managed aggregate snapshots, runtime type assertions, and C# interface method dispatch remain deferred.
- closure values are represented by Umka as an entry offset plus an `any` upvalue. UmkaSharp can register C# callbacks before compilation through Umka's closure registration path, retain exact closure values as native values, and invoke supported retained closures through `UmkaNativeValue.AsCallable()` using the fork-backed callable context APIs. The wrapper does not expose captured upvalue mutation or closure internals.
- fiber values are represented by Umka as an internal `Fiber *`. The `Fiber` struct carries VM execution state including stack pointers, registers, parent fiber, instruction pointer, VM pointer, alive state, and debug metadata. Creating and switching fibers is implemented by the Umka `make(fiber, closure)` and `resume` builtins, not by exported C API functions, so UmkaSharp exposes fiber values only as metadata until a native host API and managed ownership model exist.

## Deliberately Not Exposed

- expression-only eval: Umka's native compilation boundary is a complete source unit with declarations, imports, prototypes, and explicit result types. Use `RunSource()` / `TryRunSource()` for one-shot programs, or wrap expressions in exported typed functions and call them through `CompileSource()` plus `GetFunction()`.

## Design Status

The API is intentionally conservative. Features should be added only when ownership, lifetime, type conversion, failure behavior, tests, and native package implications are clear.
