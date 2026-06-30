# API Concepts

UmkaSharp intentionally keeps the first public API small. It favors explicit runtime ownership, explicit compilation, and typed calls over exposing raw Umka internals.

## Runtime

`UmkaRuntime` owns one embedded Umka instance. Create it from a source string:

```csharp
using var runtime = UmkaRuntime.FromSource(source);
```

Or create it from a source file:

```csharp
using var runtime = UmkaRuntime.FromFile("main.um");
```

Use the compiled factories for the shortest create-and-compile path:

```csharp
using var runtime = UmkaRuntime.CompileSource(source);
using var fileRuntime = UmkaRuntime.CompileFile("main.um");
```

When a compiled factory still needs modules or callbacks, pass `configure:`. UmkaSharp invokes the action before compilation and disposes the runtime before rethrowing if configuration or compilation fails.

```csharp
using var runtime = UmkaRuntime.CompileSource(source, configure: configured =>
{
    configured.AddModule("host.um", "fn answer*(): int");
    configured.Register("answer", _ => UmkaValue.From(42));
});
```

Use `TryCompileSource()` or `TryCompileFile()` when compile failures are expected user input and the host wants `UmkaError` data instead of an exception. These methods return an owned compiled runtime on success, return `false` with an `UmkaError` on native Umka compile errors, and dispose the transient runtime before returning failure. Invalid host arguments, configuration callback failures, warning-handler failures, and native initialization failures still throw.

File-based runtimes use Umka's normal source-file loading behavior, so imports such as `import "math.um"` resolve relative to the importing module path.

Use `UmkaRuntimeOptions` for non-default construction settings:

```csharp
using var runtime = UmkaRuntime.FromSource(source, new UmkaRuntimeOptions
{
    Arguments = ["script.um", "alpha"],
});
```

The options object controls the Umka stack size, native file-system support flag, implementation library loading flag, host-defined command-line arguments, and compile warning handler. `StackSize` must be positive. UmkaSharp snapshots `Arguments` when options are assigned and passes them through exactly as Umka command-line parameters, making them visible to `std::argc()` and `std::argv(index)`. Argument entries cannot be `null` or contain embedded NUL characters. Include the script path or program name as the first item yourself when you want CLI-style `argv(0)` behavior. Existing positional overloads remain available for simple call sites.

For logging and diagnostics, `UmkaRuntime` exposes the creation snapshot as `SourceFileName`, `StackSize`, `FileSystemEnabled`, `ImplementationLibrariesEnabled`, and `Arguments`. These properties describe how the runtime was created; changing behavior still requires creating a new runtime.

`FileSystemEnabled` defaults to `false`. With the default sandbox, Umka's embedded `std.um` can still be imported, but file, environment, and system helpers that depend on host operating-system access are routed to Umka's sandbox implementations. Set `FileSystemEnabled = true` only for scripts that should be allowed to touch the host file system. `ImplementationLibrariesEnabled` also defaults to `false`; enable it only when the host intentionally allows Umka implementation-library loading.

Set `WarningHandler` when the host wants Umka compile warnings such as unused or shadowed identifiers. Warnings are reported as `UmkaError` values with code `0`. If the warning handler throws, UmkaSharp captures that managed exception and rethrows it after native compile returns. When native compilation itself succeeded on a manually created runtime, that managed warning-handler failure does not undo compilation: the runtime is still compiled, the source graph is fixed, and exported functions can be resolved. Compiled factories own the transient runtime until they return it, so `CompileSource()`, `CompileFile()`, `TryCompileSource()`, and `TryCompileFile()` dispose that transient runtime before rethrowing a warning-handler failure.

Before `Compile()` or `TryCompile(out error)` is called, you may:

- add importable source modules with `AddModule(fileName, source)` or `AddModuleFromFile(moduleName, fileName)`
- register C# callbacks with `Register(name, callback)`

`RegisteredModuleNames` and `RegisteredCallbackNames` return sorted managed snapshots of the modules and callbacks registered directly on the runtime. They are intended for host diagnostics, configuration validation, and logging; they do not include modules discovered only through Umka's own file loader. Use `GetCallback(name)` or `TryGetCallback(name, out callback)` when host code needs the runtime-owned callback handle for a registered callback name.

Use `Compile()` for exception-based host flow, or `TryCompile(out error)` when native Umka compile errors are expected user input and should be returned as `UmkaError` data. After either compile method is called, the source graph is fixed even if compilation fails. Create a new runtime to change source, modules, callbacks, or retry after a compile error. After a successful compile, use `Run()` to execute the program entry point, or `GetFunction()` / `TryGetFunction()` to call exported functions directly.

`Run()` executes Umka's `main()` function when the compiled program defines one. Use `TryRun(out exception)` when runtime failures from `main()` are expected user input and should be handled without throwing; the returned `UmkaException` preserves the `UmkaError` and any managed callback inner exception. Programs without `main()` can still expose functions for C# to call directly.

Umka scripts may use language features such as fibers internally. UmkaSharp can compile and execute those programs through `Run()` or exported function calls, but it does not expose a managed `UmkaFiber` wrapper for host-side fiber creation, resume, or status inspection. Umka fiber values are internal `Fiber *` VM state, and creation/resume/status are language builtin behavior rather than public host C API operations.

`State` reports the observed runtime lifecycle as `Created`, `CompileAttempted`, `Compiled`, `Terminated`, or `Disposed`. `IsAlive` reports whether the runtime is still available for future code execution. It becomes `false` after a native compile failure, after Umka reports a runtime error, or after the script terminates the VM. `IsDisposed` reports deterministic managed disposal state without touching the native runtime. After the runtime is no longer alive, execution and function lookup are rejected; `GetLastError()` remains available for diagnostics until disposal, and `TryGetLastError(out error)` returns `false` when the native runtime has no meaningful diagnostic to report.

Public runtime-owned handles and metadata objects such as `UmkaRuntime`, `UmkaFunction`, `UmkaCallback`, `UmkaHostHandle`, and `UmkaTypeInfo` provide diagnostic `ToString()` output intended for logs and assertions. `UmkaRuntime.ToString()` formats the same observed lifecycle value as `State`. These strings are not a serialization format.

## Evaluation Model

Use `RunSource()` for one-shot source strings that should compile, execute `main()`, and dispose the transient runtime in one call:

```csharp
UmkaRuntime.RunSource("""
    fn main() {
    }
    """);
```

`RunSource()` accepts the same source text, file name, runtime options, modules, and callback configuration pattern as `CompileSource()`. It throws `UmkaException` for native compile errors or runtime errors from `main()`.

Use `TryRunSource(out exception)` when complete-source scripts come from user input and compile or runtime failures should be returned as data. It returns `false` with an `UmkaException` for native Umka compile errors and runtime errors, and disposes the transient runtime before returning. Invalid host arguments, configuration callback failures, warning-handler failures, and native initialization failures still throw.

UmkaSharp does not expose expression-only eval. Umka's native compilation boundary is a complete source unit with declarations, imports, prototypes, and explicit result types. Hosts that need a value should wrap the expression in an exported typed function and call it through `CompileSource()` plus `GetFunction()`.

## Modules

`AddModule` registers an in-memory module source before compilation:

```csharp
runtime.AddModule("math.um", """
    fn double*(x: int): int {
        return x * 2
    }
    """);
```

The main source can then import it with Umka's normal import syntax. File names must match the names used by imports.
Use `AddModuleFromFile(moduleName, fileName)` when the root source is in memory but a module should be read from disk. `moduleName` is the logical file name used by Umka imports, while `fileName` is the host file path to read. The single-argument `AddModuleFromFile(fileName)` overload uses the same value for both.
Umka import aliases are supported by the language and work with in-memory modules:

```umka
import m = "math.um"
```

`GetFunction(name, moduleName)` and `TryGetFunction(name, moduleName, out function)` can resolve exported functions from compiled modules that are visible to the root source. Pass the registered module file name, such as `"math.um"`, not the local import alias. A transitive module that is imported only by another module should be reached through an exported function on a root-visible module.

## Functions

`GetFunction(name, moduleName: null)` returns an `UmkaFunction` for an exported Umka function and throws `UmkaException` when the function is missing. Functions called from C# must use Umka's `*` export mark; non-exported helpers remain callable from Umka code but are not resolved by UmkaSharp lookup. `TryGetFunction(name, out function)` returns `false` for missing optional root-source functions, while `TryGetFunction(name, moduleName, out function)` does the same for module functions. A missing optional hook reported through `TryGetFunction` does not terminate the runtime or prevent later valid calls. Both lookup styles preserve normal exceptions for invalid state, invalid arguments, or native Umka errors. `UmkaFunction.Name` is the exported function name, `ModuleName` is the module file name used for lookup or `null` for root-source functions, and `QualifiedName` is `Name` for root-source functions or `moduleName::Name` for module functions. Managed validation errors use that module-qualified identity when available. The current API exposes typed result readers:

- `CallVoid`
- `TryCallVoid`
- `CallSByte`
- `CallInt16`
- `CallInt32`
- `CallInt64`
- `CallByte`
- `CallUInt16`
- `CallUInt32`
- `CallUInt64`
- `CallSingle`
- `CallDouble`
- `CallChar`
- `CallEnum<TEnum>`
- `CallBoolean`
- `CallString`
- `CallPointer`
- `CallWeakPointer`
- `TryCallWeakPointer`
- `CallScalar<T>`
- `TryCallScalar<T>`
- `CallHostObject<T>`
- `TryCallHostObject<T>`
- `CallStruct<T>`
- `CallArray<TElement>`
- `CallDynamicArray<TElement>`
- `CallStringArray`
- `CallNestedDynamicArray<TElement>`
- `CallNestedStringArray`
- `CallMap<TKey, TValue>`
- `CallDynamicArrayValueMap<TKey, TElement>`
- `CallStringKeyDynamicArrayValueMap<TElement>`
- `CallStringArrayValueMap<TKey>`
- `CallStringKeyStringArrayValueMap`
- `TryCallStruct<T>`
- `TryCallArray<TElement>`
- `TryCallDynamicArray<TElement>`
- `TryCallStringArray`
- `TryCallNestedDynamicArray<TElement>`
- `TryCallNestedStringArray`
- `TryCallMap<TKey, TValue>`
- `TryCallDynamicArrayValueMap<TKey, TElement>`
- `TryCallStringKeyDynamicArrayValueMap<TElement>`
- `TryCallStringArrayValueMap<TKey>`
- `TryCallStringKeyStringArrayValueMap`
- `CallValue`
- `TryCallValue`

Each method accepts `params UmkaValue[]` for ordinary call sites and a `ReadOnlySpan<UmkaValue>` overload for reusable argument buffers. `ParameterCount` reports the total number of explicit Umka parameters expected by the function, `RequiredParameterCount` reports the minimum C# argument count after trailing supported defaults are considered, and `DefaultParameterCount` reports how many trailing Umka parameters declare defaults. `ParameterTypes` exposes a read-only managed metadata view for each parameter, and `ResultType` exposes the result metadata. Each `UmkaTypeInfo` includes the broad managed kind, nonblank native type name, nonnegative native byte size when available, nonnegative native item count when available, whether the type contains Umka-managed references, array-like element metadata, nested dynamic-array inner element metadata, map key/value metadata, map dynamic-array value element metadata, `IsVariadicParameterList` for variadic dynamic-array parameters, `IsEnum` plus `EnumMembers` for enum member metadata, derived `IsScalar`, `IsAggregate`, and `IsDeferred` category flags for generic host dispatch, non-executing `CanReadAsValue()`, `CanReadAsScalar<T>()`, `CanReadAsWeakPointer()`, `CanReadAsStruct<T>()`, `CanReadAsFixedLayout<T>()`, `CanReadAsArray<TElement>(length)`, `CanReadAsDynamicArray<TElement>()`, `CanReadAsStringArray()`, `CanReadAsNestedDynamicArray<TElement>()`, `CanReadAsNestedStringArray()`, `CanReadAsMap<TKey, TValue>()`, `CanReadAsDynamicArrayValueMap<TKey, TElement>()`, `CanReadAsStringKeyDynamicArrayValueMap<TElement>()`, `CanReadAsStringArrayValueMap<TKey>()`, and `CanReadAsStringKeyStringArrayValueMap()` capability checks, and concise diagnostic formatting for logs. Function metadata is captured when the exported function is resolved, so it remains useful for diagnostics even after runtime disposal, but every `Call*` method still requires the owning runtime to be alive and undisposed.

Use `CanCallWith(arguments)` when generic host code needs to preflight argument count, supported argument kinds, narrow ranges, and fixed-layout aggregate/dynamic-array compatibility without executing a function. Use `CanReadResultAsValue()`, `CanReadResultAsScalar<T>()`, `CanReadResultAsWeakPointer()`, `CanReadResultAsStruct<T>()`, `CanReadResultAsArray<TElement>(length)`, `CanReadResultAsDynamicArray<TElement>()`, `CanReadResultAsStringArray()`, `CanReadResultAsNestedDynamicArray<TElement>()`, `CanReadResultAsNestedStringArray()`, `CanReadResultAsMap<TKey, TValue>()`, `CanReadResultAsDynamicArrayValueMap<TKey, TElement>()`, `CanReadResultAsStringKeyDynamicArrayValueMap<TElement>()`, `CanReadResultAsStringArrayValueMap<TKey>()`, and `CanReadResultAsStringKeyStringArrayValueMap()` to branch from cached result metadata before execution. These checks do not call into Umka and remain available with the function metadata snapshot after runtime disposal; actual `Call*` and `TryCall*` methods still enforce lifecycle, thread-affinity, argument validation, and native execution errors.

UmkaSharp can synthesize omitted trailing Umka defaults when the default parameter type is scalar, string, or pointer. Omitted defaults for dynamic arrays, maps, interfaces, closures, fibers, `any`, and reference-bearing aggregates are rejected because omitted heap/reference defaults require ownership rules that are not exposed. Current Umka source cannot declare weak pointer default parameters: `null` and weak pointer casts are rejected in default expressions because conversion to a weak pointer is not allowed in constant expressions. Variadic parameters are exposed as dynamic-array metadata. Callers may pass one explicit `UmkaValue.FromDynamicArray<TElement>(...)` value, or pass expanded trailing `UmkaValue` arguments when the variadic element kind is a supported scalar, pointer, fixed-layout struct, or fixed-layout static array without Umka-managed references. For `..str`, pass one explicit `UmkaValue.FromDynamicArray(string?[])`; expanded C# string arguments are not packed into a variadic `[]str` yet.

UmkaSharp validates argument count, supported argument kinds, narrow integer ranges, fixed-layout aggregate size/count, dynamic-array element layout, string-array shape, nested dynamic-array inner element layout, map key/value layout, dynamic-array map-value element layout, static-array result length, and result reader kind before calling into the native runtime. Narrow result helpers such as `CallSByte()` and `CallUInt16()` use checked managed conversions after the native call. `CallEnum<TEnum>()` and `TryCallEnum<TEnum>()` use the C# enum's underlying signed or unsigned storage. Enum names and declared values are available through `UmkaTypeInfo.EnumMembers` for diagnostics and generic dispatch, but numeric enum calls are not restricted to declared members. `CallScalar<T>()` is a strict convenience wrapper over the explicit scalar readers for supported integer, real, Boolean, character, string, pointer, enum, and `UmkaValue` results. `TryCallScalar<T>(out value)` returns `false` for unsupported scalar target types, incompatible result metadata, and result conversion overflow, while preserving argument validation, lifecycle, thread-affinity, and Umka execution errors. `CallValue()` returns an `UmkaValue` for supported scalar, string, pointer, weak pointer, and void results when generic host code wants a dynamic result container. `TryCallValue(out value)` returns `false` for unsupported dynamic result metadata while preserving argument validation, lifecycle, thread-affinity, and Umka execution errors. `CallWeakPointer()` and `TryCallWeakPointer(out value)` read `weak ^T` results as opaque 64-bit Umka handles. `CallVoid()` ignores void and scalar results but rejects structured, dynamic-array, and map results; `TryCallVoid()` returns `false` for structured, dynamic-array, or map result metadata while preserving argument validation, lifecycle, thread-affinity, and Umka execution errors. Struct results must be read with `CallStruct<T>()` or `TryCallStruct<T>(out value)`. Static array results can be read with `CallArray<TElement>(length)` or `TryCallArray<TElement>(length, out value)`, or as fixed-layout interop structs with `CallStruct<T>()`/`TryCallStruct<T>()`. Dynamic array results can be read with `CallDynamicArray<TElement>()`/`TryCallDynamicArray<TElement>(out value)` for reference-free elements, `CallStringArray()`/`TryCallStringArray(out value)` for `[]str`, `CallNestedDynamicArray<TElement>()`/`TryCallNestedDynamicArray<TElement>(out value)` for nested dynamic arrays whose inner element type has no Umka-managed references, and `CallNestedStringArray()`/`TryCallNestedStringArray(out value)` for `[][]str`. Fixed-layout map results can be read with `CallMap<TKey, TValue>()` or `TryCallMap<TKey, TValue>(out value)`; direct `str` map keys/values use `CallStringKeyMap<TValue>()`, `CallStringValueMap<TKey>()`, `CallStringMap()`, or matching try-style methods; maps with dynamic-array values use `CallDynamicArrayValueMap<TKey, TElement>()`, `TryCallDynamicArrayValueMap<TKey, TElement>(out value)`, `CallStringKeyDynamicArrayValueMap<TElement>()`, or `TryCallStringKeyDynamicArrayValueMap<TElement>(out value)` when the array element type has no Umka-managed references, and `CallStringArrayValueMap<TKey>()`, `TryCallStringArrayValueMap<TKey>(out value)`, `CallStringKeyStringArrayValueMap()`, or `TryCallStringKeyStringArrayValueMap(out value)` when the value type is `[]str`. Structured, dynamic-array, nested-array, and map result target types must be unmanaged fixed-layout shapes whose marshalled size exactly matches the Umka result, element, inner element, key, value, or dynamic-array map-value element size, except for direct `str` copy paths. The try-style structured, dynamic-array, string-array, nested-array, and map readers return `false` for incompatible result metadata, unsupported reference-bearing aggregates, elements, keys, values, or dynamic-array map-value elements, wrong static-array length, or mismatched managed layout while preserving argument validation, lifecycle, thread-affinity, and Umka execution errors. Scalar result readers reject incompatible result types.

## Values

`UmkaValue` is the dynamic managed argument, scalar result, and callback-result container. It supports signed integers, unsigned integers, reals, booleans, strings, pointers, weak pointer handles, runtime-owned host handles, fixed-layout structs, static arrays, dynamic arrays, `[]str`, nested dynamic arrays, `[][]str`, and void callback results. The value kind must match the resolved Umka parameter type; for example, pass `UmkaValue.From(int)` or `UmkaValue.From(long)` to signed integer parameters, `UmkaValue.From(uint)` or `UmkaValue.From(ulong)` to unsigned integer parameters, `UmkaValue.FromWeakPointer(handle)` to weak pointer parameters, `UmkaValue.FromStruct(value)` to struct parameters, `UmkaValue.FromDynamicArray<TElement>(values)` to reference-free dynamic-array parameters, `UmkaValue.FromDynamicArray(string?[])` to `[]str` parameters, `UmkaValue.FromNestedDynamicArray<TElement>(values)` to reference-free nested dynamic-array parameters, and `UmkaValue.FromNestedDynamicArray(string?[][])` to `[][]str` parameters. Generic host code can use `UmkaValue.FromScalar<T>(value)` or `UmkaValue.TryFromScalar<T>(value, out result)` for supported scalar, string, pointer, enum, host-handle, or existing `UmkaValue` inputs, and `UmkaValue.AsScalar<T>()` or `UmkaValue.TryAsScalar<T>(out value)` for supported scalar, string, pointer, enum, or existing `UmkaValue` reads. Weak pointer handles stay explicit through `FromWeakPointer(ulong)`, `AsWeakPointer()`, and `TryAsWeakPointer(out value)`. Structs, static arrays, and dynamic arrays stay explicit through `FromStruct<T>()`, `TryFromStruct<T>(value, out result)`, `FromStaticArray<TElement>()`, `TryFromStaticArray<TElement>(values, out result)`, `FromDynamicArray<TElement>()`, `TryFromDynamicArray<TElement>(values, out result)`, `FromDynamicArray(string?[])`, `TryFromDynamicArray(string?[]?, out result)`, `FromNestedDynamicArray<TElement>()`, `TryFromNestedDynamicArray<TElement>(values, out result)`, `FromNestedDynamicArray(string?[][])`, `TryFromNestedDynamicArray(string?[][]?, out result)`, `AsStruct<T>()`, `TryAsStruct<T>(out value)`, `AsStaticArray<TElement>()`, `TryAsStaticArray<TElement>(out value)`, `AsDynamicArray<TElement>()`, `TryAsDynamicArray<TElement>(out value)`, `AsStringArray()`, `TryAsStringArray(out value)`, `AsNestedDynamicArray<TElement>()`, `TryAsNestedDynamicArray<TElement>(out value)`, `AsNestedStringArray()`, and `TryAsNestedStringArray(out value)`.

Umka enum values are marshalled through their underlying signed or unsigned integer storage. Use `UmkaValue.FromEnum<TEnum>`, `UmkaValue.AsEnum<TEnum>`/`TryAsEnum<TEnum>`, `UmkaFunction.CallEnum<TEnum>`/`TryCallEnum<TEnum>`, and `UmkaCallFrame.GetEnum<TEnum>`/`TryGetEnum<TEnum>` when a C# enum with matching underlying storage makes the call site clearer. Use `UmkaTypeInfo.IsEnum` and `EnumMembers` on function or callback metadata when host code needs Umka enum member names and declared numeric values.

For diagnostics and reusable host-side code, `UmkaValue` also exposes checked readers such as `AsSByte`, `AsInt32`, `AsByte`, `AsUInt32`, `AsChar`, `AsEnum<TEnum>`, and `AsSingle`, alongside the broad `AsInt64`, `AsUInt64`, `AsDouble`, `AsBoolean`, `AsString`, and `AsPointer` readers. `TryFromScalar<T>(value, out result)` returns `false` instead of throwing for unsupported input types, unsupported null inputs, embedded-NUL strings, or out-of-range C# `char` values. `TryAsEnum<TEnum>(out value)` returns `false` instead of throwing for wrong enum storage or range failures. `AsScalar<T>()` dispatches to checked readers for generic host code that knows the managed scalar type, and `TryAsScalar<T>(out value)` returns `false` instead of throwing for unsupported target types, wrong value kinds, or range failures. `AsSingle` rejects finite values outside the `System.Single` range. Fixed-layout payloads can be created with `FromStruct<T>()`, `FromStaticArray<TElement>()`, `FromDynamicArray<TElement>()`, and `FromNestedDynamicArray<TElement>()`; `[]str` payloads use `FromDynamicArray(string?[])`, and `[][]str` payloads use `FromNestedDynamicArray(string?[][])`. The matching try-style factories return `false` for null array inputs, null nested-array rows, managed-reference payload types, embedded-NUL string values, or layout size overflow. Fixed-layout payloads can be inspected with `AsStruct<T>`, `AsStaticArray<TElement>`, `AsDynamicArray<TElement>`, and `AsNestedDynamicArray<TElement>`, which return the original managed struct type or a defensive array copy. `[]str` and `[][]str` payloads can be inspected with `AsStringArray()` and `AsNestedStringArray()`. `TryAsStruct<T>`, `TryAsStaticArray<TElement>`, `TryAsDynamicArray<TElement>`, `TryAsNestedDynamicArray<TElement>`, `TryAsStringArray`, and `TryAsNestedStringArray` return `false` instead of throwing for wrong value kinds, wrong managed snapshot types, or managed-reference target types. Static-array, dynamic-array, and nested dynamic-array values can be created from `params`, arrays, `Span<TElement>`, or `ReadOnlySpan<TElement>` and are snapshotted on creation.

`UmkaValue.ToString()` formats scalar values directly and summarizes fixed-layout structs and static arrays by kind, size, and length instead of exposing mutable aggregate contents.

This is not a full Umka heap value wrapper. UmkaSharp copies supported reference-free dynamic arrays, `[]str`, and nested dynamic arrays with reference-free or direct-`str` inner elements across the boundary, copies fixed-layout maps, direct-string maps, and maps whose values are supported dynamic arrays or `[]str` in supported copy-out directions, and copies weak pointers only as opaque 64-bit handles. It does not expose long-lived references to Umka arrays, maps, interfaces, fibers, or `any` values, and it does not strengthen, validate, root, or own weak pointer targets. Closure values are metadata-only: Umka represents them as an entry offset plus captured `any` upvalue state, and UmkaSharp does not expose C# invocation or retention for arbitrary closure values. Fiber values are also metadata-only because their native value is an internal `Fiber *` with VM stack and scheduling state.

## Callbacks

`UmkaCallFrame` exposes callback `ParameterCount`, read-only `ParameterTypes`, and `ResultType`, with the same kind, type-name, native-size, item-count, reference-bearing, array element, nested dynamic-array inner element, map key/value, map dynamic-array value element, variadic, enum, and category metadata as exported functions. Its typed readers validate the argument index and broad Umka type before reading a native callback slot, with checked helpers for narrow integers, C# enums through `GetEnum<TEnum>()`/`TryGetEnum<TEnum>()`, `char`, finite `real32` conversion, dynamic supported value reads through `GetValue()` and `TryGetValue()`, weak pointer reads through `GetWeakPointer()` and `TryGetWeakPointer()`, dynamic-array reads through `GetDynamicArray<TElement>()`, `TryGetDynamicArray<TElement>(index, out value)`, `GetStringArray(index)`, `TryGetStringArray(index, out value)`, `GetNestedDynamicArray<TElement>(index)`, `TryGetNestedDynamicArray<TElement>(index, out value)`, `GetNestedStringArray(index)`, and `TryGetNestedStringArray(index, out value)`, map reads through `GetMap<TKey, TValue>()`, `TryGetMap<TKey, TValue>(index, out value)`, `GetStringKeyMap<TValue>()`, `GetStringValueMap<TKey>()`, `GetStringMap()`, `GetDynamicArrayValueMap<TKey, TElement>()`, `TryGetDynamicArrayValueMap<TKey, TElement>(index, out value)`, `GetStringKeyDynamicArrayValueMap<TElement>()`, `TryGetStringKeyDynamicArrayValueMap<TElement>(index, out value)`, `GetStringArrayValueMap<TKey>()`, `TryGetStringArrayValueMap<TKey>(index, out value)`, `GetStringKeyStringArrayValueMap()`, and `TryGetStringKeyStringArrayValueMap(index, out value)`, strict generic scalar reads through `GetScalar<T>()`, and try-style generic scalar reads through `TryGetScalar<T>(index, out value)`. `CanReadArgumentAsValue(index)`, `CanReadArgumentAsScalar<T>(index)`, `CanReadArgumentAsWeakPointer(index)`, `CanReadArgumentAsStruct<T>(index)`, `CanReadArgumentAsArray<TElement>(index, length)`, `CanReadArgumentAsDynamicArray<TElement>(index)`, `CanReadArgumentAsStringArray(index)`, `CanReadArgumentAsNestedDynamicArray<TElement>(index)`, `CanReadArgumentAsNestedStringArray(index)`, `CanReadArgumentAsMap<TKey, TValue>(index)`, `CanReadArgumentAsDynamicArrayValueMap<TKey, TElement>(index)`, `CanReadArgumentAsStringKeyDynamicArrayValueMap<TElement>(index)`, `CanReadArgumentAsStringArrayValueMap<TKey>(index)`, `CanReadArgumentAsStringKeyStringArrayValueMap(index)`, and the string-map preflight helpers inspect callback argument metadata before reading it; value-dependent range failures still belong to the actual read. `CanReturn(value)` preflights a candidate callback result against the active frame's result metadata before writing it. Callback frames are active only while the callback is executing; stored frame copies reject later reads before touching native callback slots. Metadata objects copied from `ParameterTypes` or `ResultType` inside the callback are managed snapshots and may be retained. Callback return values are validated before they are written to the native result slot.

`Register(name, callback)` returns a runtime-owned `UmkaCallback` handle. Use `RegisterVoid(name, callback)` for Umka void prototypes when the C# side should be an `Action<UmkaCallFrame>` instead of returning `UmkaValue.Void` manually. The runtime keeps the managed delegate alive until disposal, and missed runtime disposal has a finalizer fallback for native cleanup. Use `Name` and `IsDisposed` for diagnostics and lifecycle checks, `GetCallback(name)` or `TryGetCallback(name, out callback)` to recover a registered callback handle by name, `UmkaCallback.LastException` to inspect the last failure from a specific callback handle, and `UmkaRuntime.LastCallbackException` to inspect the last managed callback failure observed by the runtime. When a callback failure reaches the host as `UmkaException`, the same managed exception is attached as `InnerException`.

Use `CreateHostHandle(target)` when Umka code should carry an opaque managed object reference back through a pointer value. Creating new handles requires a live, undisposed runtime that is not in diagnostic-only state after a native compile failure. Existing handles remain managed resources after a compile failure or Umka runtime error: they can still be inspected, resolved, and disposed on the owning thread until the runtime itself is disposed. Pass the handle with `UmkaValue.FromHostHandle(handle)`, resolve callback arguments with `frame.GetHostObject<T>(index)` or `frame.TryGetHostObject<T>(index, out target)`, read known host-handle pointer results with `function.CallHostObject<T>()`, and use `function.TryCallHostObject<T>(out target)` when a null, unknown, disposed, or wrong-type returned handle should be handled as data. Other known handle addresses can be resolved with `runtime.GetHostObject<T>(pointer)` or `runtime.TryGetHostObject<T>(pointer, out target)`. Direct handle reads use `handle.GetTarget<T>()` or `handle.TryGetTarget<T>(out target)`. The try-style host-handle helpers return `false` for null, unknown, disposed, or wrong-type handle pointers while preserving thread-affinity, type, and Umka execution checks. `UmkaHostHandle.IsDisposed` reports whether the runtime-owned handle is still usable.

## Native Boundary

Managed code calls `umka_shim`, a C ABI layer built from this repository and the Umka C sources. The shim centralizes calls into Umka, translates basic errors, and keeps native details out of the public managed API.
