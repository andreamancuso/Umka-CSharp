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

`UmkaCallFrame` exposes `ParameterCount`, read-only `ParameterTypes`, and `ResultType`, and provides typed readers. It is valid only while the managed callback is executing; copy the values or metadata you need before returning. Attempts to read a stored frame after the callback returns are rejected before touching native callback slots, including metadata reads and host-handle resolution through `GetHostObject<T>` or `TryGetHostObject<T>`. The `ParameterTypes` list and `ResultType` value returned inside the callback are managed metadata snapshots and can be retained for logging or diagnostics. Each `UmkaTypeInfo` includes the broad managed kind, native type name, native byte size when available, native item count when available, whether the type contains Umka-managed references, array-like element metadata, nested dynamic-array inner element metadata, map key/value metadata, map dynamic-array value element metadata, variadic dynamic-array marker metadata, enum marker/member metadata when the type is an enum, and derived `IsScalar`, `IsAggregate`, and `IsDeferred` category flags:

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
- `GetWeakPointer`
- `TryGetWeakPointer`
- `GetValue`
- `TryGetValue`
- `GetDynamicArray<TElement>`
- `TryGetDynamicArray<TElement>`
- `GetNestedDynamicArray<TElement>`
- `TryGetNestedDynamicArray<TElement>`
- `GetNestedStringArray`
- `TryGetNestedStringArray`
- `GetMap<TKey, TValue>`
- `TryGetMap<TKey, TValue>`
- `GetStringKeyMap<TValue>`
- `TryGetStringKeyMap<TValue>`
- `GetStringValueMap<TKey>`
- `TryGetStringValueMap<TKey>`
- `GetStringMap`
- `TryGetStringMap`
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
- `CanReadArgumentAsWeakPointer`
- `CanReadArgumentAsStruct<T>`
- `CanReadArgumentAsArray<TElement>`
- `CanReadArgumentAsDynamicArray<TElement>`
- `CanReadArgumentAsNestedDynamicArray<TElement>`
- `CanReadArgumentAsNestedStringArray`
- `CanReadArgumentAsMap<TKey, TValue>`

The callback reader must match the Umka prototype. UmkaSharp validates argument indexes and reader kinds before reading native callback slots. A wrong reader, such as `GetInt64(0)` for a `str` parameter, is captured as a managed callback failure.

Narrow integer readers use checked managed conversions after the native slot is read. `GetSingle` reads an Umka `real32` or `real` argument and returns `float`, rejecting finite values outside the `System.Single` range; `GetChar` requires an Umka `char` argument. `GetEnum<TEnum>` and `TryGetEnum<TEnum>` read Umka enum arguments through matching signed or unsigned C# enum storage. `GetValue` reads supported scalar, string, pointer, and weak pointer callback arguments into an `UmkaValue` for generic dispatch. `GetWeakPointer` reads `weak ^T` values as opaque 64-bit Umka handles. `TryGetValue` preserves frame lifecycle and index errors, but returns `false` for unsupported callback argument kinds. `GetScalar<T>` is a strict convenience reader for generic host code that knows the managed scalar, string, pointer, enum, or `UmkaValue` argument type. `TryGetScalar<T>` preserves frame lifecycle and index errors, but returns `false` for unsupported target types, wrong Umka kinds, and range failures.

Use `CanReadArgumentAsValue`, `CanReadArgumentAsScalar<T>`, `CanReadArgumentAsWeakPointer`, `CanReadArgumentAsStruct<T>`, `CanReadArgumentAsArray<TElement>`, `CanReadArgumentAsDynamicArray<TElement>`, `CanReadArgumentAsNestedDynamicArray<TElement>`, `CanReadArgumentAsNestedStringArray`, `CanReadArgumentAsMap<TKey, TValue>`, `CanReadArgumentAsDynamicArrayValueMap<TKey, TElement>`, `CanReadArgumentAsStringKeyDynamicArrayValueMap<TElement>`, `CanReadArgumentAsStringArrayValueMap<TKey>`, and `CanReadArgumentAsStringKeyStringArrayValueMap` when generic callback code needs to preflight argument metadata before choosing a strict reader. These helpers preserve frame lifecycle and index errors, but do not read native argument values; narrow integer, enum, `char`, and finite `real32` range failures can still happen when the value is actually read.

Umka enum arguments are read through their underlying integer storage. Use `GetEnum<TEnum>` or `TryGetEnum<TEnum>` when a C# enum has matching signed or unsigned underlying storage, or use the corresponding signed/unsigned integer reader directly. Use `ParameterTypes[index].EnumMembers` or `ResultType.EnumMembers` when callback code needs the Umka enum member names or declared numeric values for diagnostics or dispatch.

`GetString` returns `null` for a null Umka string. `GetPointer` returns `IntPtr.Zero` for a null Umka pointer; non-null pointers are opaque and remain owned by Umka or the host that created them.

`GetHostObject<T>` resolves a pointer parameter that was created with `UmkaRuntime.CreateHostHandle` and passed with `UmkaValue.FromHostHandle(handle)`. The handle must belong to the same runtime and must still be alive. Unknown, null, disposed, or wrong-type handles are captured as managed callback failures. Use `TryGetHostObject<T>` when null, unknown, disposed, or wrong-type handle values are expected input and should be handled by the callback instead of failing the Umka call.

`GetStruct<T>`, `GetArray<TElement>`, `GetDynamicArray<TElement>`, `GetStringArray`, `GetNestedDynamicArray<TElement>`, `GetNestedStringArray`, `GetMap<TKey, TValue>`, the string-map readers, `GetDynamicArrayValueMap<TKey, TElement>`, `GetStringKeyDynamicArrayValueMap<TElement>`, `GetStringArrayValueMap<TKey>`, and `GetStringKeyStringArrayValueMap` copy supported Umka aggregate, dynamic-array, string-array, nested-array, or map arguments into managed values. The managed size must match the native Umka size exactly, static array lengths must match, dynamic-array element size must match, nested dynamic-array inner element size must match, map key/value sizes must match, dynamic-array map-value element sizes must match, and `TElement`, `TKey`, or `TValue` may be a sequential fixed-layout struct when the Umka layout matches exactly. `TryGetStruct<T>`, `TryGetArray<TElement>`, `TryGetDynamicArray<TElement>`, `TryGetStringArray`, `TryGetNestedDynamicArray<TElement>`, `TryGetNestedStringArray`, `TryGetMap<TKey, TValue>`, the string-map try readers, `TryGetDynamicArrayValueMap<TKey, TElement>`, `TryGetStringKeyDynamicArrayValueMap<TElement>`, `TryGetStringArrayValueMap<TKey>`, and `TryGetStringKeyStringArrayValueMap` preserve frame lifecycle and index errors, but return `false` for wrong aggregate kinds, wrong static-array length, mismatched managed layout, managed-reference target types, and Umka types that contain unsupported managed references such as maps of interfaces, maps of closures, maps of fibers, or maps of `any`. Direct `str` values are supported as `[]str` elements, `[][]str` inner elements, direct map keys or values, and `[]str` map-value elements. Other dynamic-array map values are supported only when the array element type contains no Umka-managed references. Nested dynamic-array callback arguments and callback results are supported only when the inner element type contains no Umka-managed references or is direct `str`; callback results use `UmkaValue.FromNestedDynamicArray(...)`.

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

Callback results are validated against the Umka prototype before UmkaSharp writes to the native result slot. Use `frame.CanReturn(value)` inside the callback when generic host code needs to preflight a candidate `UmkaValue` against the active callback result metadata before returning it. Enum results use the same underlying integer rules as enum arguments; return them with `UmkaValue.FromEnum<TEnum>` or an explicit signed/unsigned integer value. Weak pointer results can be returned with `UmkaValue.FromWeakPointer(handle)`, where the handle is treated as an opaque 64-bit Umka value. Fixed-layout static array, dynamic-array, `[]str`, nested dynamic-array, `[][]str`, and struct results can be returned with `UmkaValue.FromStaticArray<TElement>`, `UmkaValue.FromDynamicArray<TElement>`, `UmkaValue.FromDynamicArray(string?[])`, `UmkaValue.FromNestedDynamicArray<TElement>`, `UmkaValue.FromNestedDynamicArray(string?[][])`, and `UmkaValue.FromStruct<T>` when the managed size, element size, row lengths, and array length where applicable exactly match Umka and the Umka type has no unsupported managed-reference fields or elements; weak pointer fields and static-array elements use `ulong`, and static, dynamic-array, and nested dynamic-array elements may be sequential fixed-layout structs. Deferred result kinds such as maps, `any`, closures, and fibers are exposed through `ResultType`, but rejected before writing a managed result. Map callback results remain unsupported because Umka's public C API does not expose safe host-side map creation, insertion, rooting, ownership transfer, or assignment/reference-count updates. Mismatched `UmkaValue` kinds, unsupported reference-bearing aggregate, dynamic-array element, nested dynamic-array inner element, or map key/value result types, and out-of-range narrow integer, enum, `char`, or finite `real32` results are captured as managed callback failures.

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
