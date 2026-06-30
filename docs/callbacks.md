# Callbacks

Callbacks let Umka call C# functions registered before compilation.

## Register A Callback

Declare the callback prototype in Umka source and register a C# function with the same exported name:

```csharp
using var runtime = UmkaRuntime.FromSource("""
    fn doubleIt*(x: int): int

    fn answer*(): int {
        return doubleIt(21)
    }
    """);

var callback = runtime.Register("doubleIt", frame =>
    UmkaValue.From(frame.GetInt64(0) * 2));

runtime.Compile();

Console.WriteLine(runtime.GetFunction("answer").CallInt64());
```

The returned `UmkaCallback` is owned by the runtime. It exposes the registered `Name`, an `IsDisposed` lifecycle flag, and `LastException` for diagnostics. `UmkaRuntime.LastCallbackException` exposes the last managed callback failure observed by the runtime when host code did not keep a specific callback handle. Keep the runtime alive for as long as Umka code may call the callback.
Callback names are registered once per runtime before compilation. Duplicate names and names reserved by Umka are rejected during registration.

For Umka prototypes with no result value, `RegisterVoid` accepts an `Action<UmkaCallFrame>` and returns `UmkaValue.Void` for you:

```csharp
runtime.RegisterVoid("notify", frame =>
{
    Console.WriteLine(frame.GetString(0));
});
```

Use `UmkaRuntime.RegisteredCallbackNames` when host code needs a sorted diagnostic snapshot of callbacks registered directly on a runtime. Use `GetCallback(name)` or `TryGetCallback(name, out callback)` when host code needs the runtime-owned callback handle for a registered name, including after disposal for diagnostics.

## Reading Arguments

`UmkaCallFrame` exposes `ParameterCount`, read-only `ParameterTypes`, and `ResultType`, and provides typed readers. It is valid only while the managed callback is executing; copy the values or metadata you need before returning. Attempts to read a stored frame after the callback returns are rejected before touching native callback slots, including metadata reads and host-handle resolution through `GetHostObject<T>` or `TryGetHostObject<T>`. The `ParameterTypes` list and `ResultType` value returned inside the callback are managed metadata snapshots and can be retained for logging or diagnostics. Each `UmkaTypeInfo` includes the broad managed kind, native type name, native byte size when available, native item count when available, whether the type contains Umka-managed references, and derived `IsScalar`, `IsAggregate`, and `IsDeferred` category flags:

- `GetInt64`
- `GetSByte`
- `GetInt16`
- `GetInt32`
- `GetUInt64`
- `GetByte`
- `GetUInt16`
- `GetUInt32`
- `GetDouble`
- `GetSingle`
- `GetChar`
- `GetEnum<TEnum>`
- `TryGetEnum<TEnum>`
- `GetBoolean`
- `GetString`
- `GetPointer`
- `GetValue`
- `TryGetValue`
- `GetScalar<T>`
- `TryGetScalar<T>`
- `GetHostObject<T>`
- `TryGetHostObject<T>`
- `GetStruct<T>`
- `GetArray<TElement>`
- `TryGetStruct<T>`
- `TryGetArray<TElement>`
- `CanReadArgumentAsValue`
- `CanReadArgumentAsScalar<T>`
- `CanReadArgumentAsStruct<T>`
- `CanReadArgumentAsArray<TElement>`

The callback reader must match the Umka prototype. UmkaSharp validates argument indexes and reader kinds before reading native callback slots. A wrong reader, such as `GetInt64(0)` for a `str` parameter, is captured as a managed callback failure.

Narrow integer readers use checked managed conversions after the native slot is read. `GetSingle` reads an Umka `real32` or `real` argument and returns `float`, rejecting finite values outside the `System.Single` range; `GetChar` requires an Umka `char` argument. `GetEnum<TEnum>` and `TryGetEnum<TEnum>` read Umka enum arguments through matching signed or unsigned C# enum storage. `GetValue` reads supported scalar, string, and pointer callback arguments into an `UmkaValue` for generic dispatch. `TryGetValue` preserves frame lifecycle and index errors, but returns `false` for unsupported callback argument kinds. `GetScalar<T>` is a strict convenience reader for generic host code that knows the managed scalar, string, pointer, enum, or `UmkaValue` argument type. `TryGetScalar<T>` preserves frame lifecycle and index errors, but returns `false` for unsupported target types, wrong Umka kinds, and range failures.

Use `CanReadArgumentAsValue`, `CanReadArgumentAsScalar<T>`, `CanReadArgumentAsStruct<T>`, and `CanReadArgumentAsArray<TElement>` when generic callback code needs to preflight argument metadata before choosing a strict reader. These helpers preserve frame lifecycle and index errors, but do not read native argument values; narrow integer, enum, `char`, and finite `real32` range failures can still happen when the value is actually read.

Umka enum arguments are read through their underlying integer storage. Use `GetEnum<TEnum>` or `TryGetEnum<TEnum>` when a C# enum has matching signed or unsigned underlying storage, or use the corresponding signed/unsigned integer reader directly.

`GetString` returns `null` for a null Umka string. `GetPointer` returns `IntPtr.Zero` for a null Umka pointer; non-null pointers are opaque and remain owned by Umka or the host that created them.

`GetHostObject<T>` resolves a pointer parameter that was created with `UmkaRuntime.CreateHostHandle` and passed with `UmkaValue.FromHostHandle(handle)`. The handle must belong to the same runtime and must still be alive. Unknown, null, disposed, or wrong-type handles are captured as managed callback failures. Use `TryGetHostObject<T>` when null, unknown, disposed, or wrong-type handle values are expected input and should be handled by the callback instead of failing the Umka call.

`GetStruct<T>` and `GetArray<TElement>` copy fixed-layout Umka aggregate arguments into managed values. The managed size must match the native Umka size exactly, static array lengths must match, and `TElement` may be a sequential fixed-layout struct for `[N]SomeStruct` arguments. `TryGetStruct<T>` and `TryGetArray<TElement>` preserve frame lifecycle and index errors, but return `false` for wrong aggregate kinds, wrong static-array length, mismatched managed layout, managed-reference target types, and aggregate arguments that contain Umka-managed references such as strings, dynamic arrays, maps, interfaces, closures, weak pointers, or fibers.

## Returning Values

Return an `UmkaValue` from the callback:

```csharp
runtime.Register("formatScore", frame =>
{
    var name = frame.GetString(0);
    var score = frame.GetInt64(1);
    return UmkaValue.From($"{name}: {score}");
});
```

Use `UmkaValue.Void` for callbacks that do not return a value.

Callback results are validated against the Umka prototype before UmkaSharp writes to the native result slot. Use `frame.CanReturn(value)` inside the callback when generic host code needs to preflight a candidate `UmkaValue` against the active callback result metadata before returning it. Enum results use the same underlying integer rules as enum arguments; return them with `UmkaValue.FromEnum<TEnum>` or an explicit signed/unsigned integer value. Fixed-layout static array and struct results can be returned with `UmkaValue.FromStaticArray<TElement>` and `UmkaValue.FromStruct<T>` when the managed size and array length exactly match Umka and the Umka type has no managed-reference fields; static array elements may be sequential fixed-layout structs. Deferred result kinds such as dynamic arrays, maps, `any`, closures, weak pointers, and fibers are exposed through `ResultType`, but rejected before writing a managed result. Mismatched `UmkaValue` kinds, reference-bearing aggregate result types, and out-of-range narrow integer, enum, `char`, or finite `real32` results are captured as managed callback failures.

## Exceptions

If a callback throws, UmkaSharp stores the managed exception on `UmkaCallback.LastException` and `UmkaRuntime.LastCallbackException`, reports failure to Umka, and attaches the managed exception as `UmkaException.InnerException` on the failing call.

```csharp
var callback = runtime.Register("fail", _ => throw new InvalidOperationException("boom"));

try
{
    runtime.GetFunction("run").CallVoid();
}
catch (UmkaException ex)
{
    Console.WriteLine(ex.InnerException?.Message);
    Console.WriteLine(callback.LastException?.Message);
    Console.WriteLine(runtime.LastCallbackException?.Message);
}
```

Callback exceptions should be treated as runtime failures. The failing Umka call terminates the runtime, `IsAlive` becomes `false`, and later execution or function lookup attempts are rejected; create a new runtime for more work.
