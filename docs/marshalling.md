# Marshalling

UmkaSharp exposes an explicit, conservative marshalling layer through `UmkaValue`, `UmkaFunction`, and `UmkaCallFrame`.

## Supported Values

| Umka kind | C# call argument | C# result reader | Callback reader/result |
| --- | --- | --- | --- |
| `int` | `UmkaValue.From(sbyte/short/int/long)` | `CallSByte`, `CallInt16`, `CallInt32`, `CallInt64`, or `CallScalar<T>` | `GetSByte`, `GetInt16`, `GetInt32`, `GetInt64`, or `GetScalar<T>` / `UmkaValue.From(sbyte/short/int/long)` |
| `uint` | `UmkaValue.From(byte/ushort/uint/ulong/char)` | `CallByte`, `CallUInt16`, `CallUInt32`, `CallUInt64`, or `CallScalar<T>` | `GetByte`, `GetUInt16`, `GetUInt32`, `GetUInt64`, or `GetScalar<T>` / `UmkaValue.From(byte/ushort/uint/ulong/char)` |
| `char` | `UmkaValue.From(char)` for byte-range C# chars, or byte-range integer values | `CallChar`, `CallByte`, `CallUInt64`, or `CallScalar<T>` | `GetChar`, `GetByte`, `GetUInt64`, or `GetScalar<char>` / `UmkaValue.From(char)` |
| `real` | `UmkaValue.From(float/double)` | `CallSingle`, `CallDouble`, or `CallScalar<T>` | `GetSingle`, `GetDouble`, or `GetScalar<T>` / `UmkaValue.From(float/double)` |
| `bool` | `UmkaValue.From(bool)` | `CallBoolean` or `CallScalar<T>` | `GetBoolean` or `GetScalar<bool>` / `UmkaValue.From(bool)` |
| `str` | `UmkaValue.From(string?)` | `CallString` or `CallScalar<string>` | `GetString` or `GetScalar<string>` / `UmkaValue.From(string?)` |
| enum types | `UmkaValue.FromEnum<TEnum>` or underlying signed/unsigned integer `UmkaValue.From(...)` | `CallEnum<TEnum>`, `TryCallEnum<TEnum>`, `CallScalar<TEnum>`, or matching integer `Call*` helper | `GetEnum<TEnum>`, `TryGetEnum<TEnum>`, `GetScalar<TEnum>`, or matching integer reader / `UmkaValue.FromEnum<TEnum>` |
| pointer types such as `^int` or `^void` | `UmkaValue.FromPointer(IntPtr)` or `UmkaValue.FromHostHandle(handle)` | `CallPointer`, `CallScalar<IntPtr>`, `CallHostObject<T>`, or `TryCallHostObject<T>` for runtime-owned host handles | `GetPointer`, `GetScalar<IntPtr>`, `GetHostObject<T>`, or `TryGetHostObject<T>` / `UmkaValue.FromPointer(IntPtr)` |
| static arrays | `UmkaValue.FromStaticArray<TElement>(...)` | `CallArray<TElement>(length)`, `TryCallArray<TElement>(length, out value)`, `CallStruct<T>`, or `TryCallStruct<T>` | `GetArray<TElement>(length)` or `TryGetArray<TElement>(index, length, out value)` / `UmkaValue.FromStaticArray<TElement>(...)` |
| structs | `UmkaValue.FromStruct<T>` | `CallStruct<T>` or `TryCallStruct<T>` | `GetStruct<T>` or `TryGetStruct<T>` / `UmkaValue.FromStruct<T>` |
| multiple returns such as `(int, int)` | n/a | `CallStruct<T>` or `TryCallStruct<T>` with a matching sequential struct | n/a |

`UmkaValue.FromScalar<T>()` is a convenience wrapper over the explicit scalar value factories. Use it when generic host code knows the managed scalar input type but should not choose a specific `From(...)` overload itself. `TryFromScalar<T>(value, out result)` is the try-style version for unsupported input types, unsupported null inputs, embedded-NUL strings, and out-of-range C# `char` values. Fixed-layout aggregate inputs use `FromStruct<T>()`, `TryFromStruct<T>(value, out result)`, `FromStaticArray<TElement>()`, and `TryFromStaticArray<TElement>(values, out result)`; the try-style factories return `false` for null static-array inputs, managed-reference payload types, or layout size overflow. `UmkaValue.AsScalar<T>()` is the matching strict convenience wrapper over the explicit scalar value readers, while `TryAsScalar<T>(out value)` returns `false` for unsupported target types, wrong value kinds, or range failures. Explicit enum reads also have `TryAsEnum<TEnum>(out value)`, `TryCallEnum<TEnum>(out value)`, and `TryGetEnum<TEnum>(index, out value)` helpers. `CallScalar<T>()` and `TryCallScalar<T>(out value)` provide strict and try-style generic dispatch over explicit scalar function result readers. `TryCallScalar<T>` returns `false` for unsupported scalar target types, incompatible result metadata, and result conversion overflow, while argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. `GetValue()` and `TryGetValue(out value)` read supported callback scalar, string, and pointer arguments into an `UmkaValue`; `TryGetValue` returns `false` for unsupported callback argument metadata while frame lifecycle and index errors still throw. `GetScalar<T>()` and `TryGetScalar<T>(index, out value)` provide strict and try-style generic dispatch for callback arguments when generic host code knows the managed scalar type but does not need to name the specific reader. `CallValue()` and `TryCallValue(out value)` read supported scalar, string, pointer, and void results into an `UmkaValue` for generic host-side dispatch; `TryCallValue` returns `false` for unsupported dynamic result metadata while argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. `TryCallVoid()` invokes functions with void or scalar results and returns `false` for structured result metadata; argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. Fixed-layout struct and static-array results stay explicit through `CallStruct<T>()`, `TryCallStruct<T>(out value)`, `CallArray<TElement>(length)`, and `TryCallArray<TElement>(length, out value)`. Callback aggregate arguments use `GetStruct<T>()`, `TryGetStruct<T>(index, out value)`, `GetArray<TElement>(index, length)`, and `TryGetArray<TElement>(index, length, out value)`. The try-style structured readers return `false` for incompatible result/argument metadata, reference-bearing aggregates, wrong static-array length, or mismatched managed layout while argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. Dynamic arrays, maps, interfaces, closures, weak pointers, fibers, and `any` remain unsupported as managed result values.

Strings are marshalled as UTF-8. A `null` C# string is passed as a null Umka string pointer and a null Umka string result is returned as `null`. Embedded NUL characters are rejected before crossing the native string boundary.

Umka enum types are reported as their underlying integer kind. For example, `enum` uses signed integer marshalling, while `enum(uint8)` uses unsigned byte-range marshalling. `FromEnum<TEnum>`, `AsEnum<TEnum>`/`TryAsEnum<TEnum>`, `CallEnum<TEnum>`/`TryCallEnum<TEnum>`, and `GetEnum<TEnum>`/`TryGetEnum<TEnum>` use the C# enum's underlying signed or unsigned storage. UmkaSharp does not expose Umka enum member names or validate that an integer value corresponds to a declared Umka enum constant.

Pointer values are opaque. `UmkaValue.FromPointer(IntPtr)` passes raw pointer-sized values and UmkaSharp does not own or validate the memory behind them. For managed host objects, prefer `UmkaRuntime.CreateHostHandle(target)` plus `UmkaValue.FromHostHandle(handle)` and `UmkaCallFrame.GetHostObject<T>(index)`, which validate that the pointer belongs to the current runtime. Use `TryGetHostObject<T>` when a null, unknown, disposed, or wrong-type handle is normal input and should return `false` instead of throwing.

## Function Type Metadata

`UmkaFunction.ParameterCount` exposes the number of explicit Umka arguments expected by a resolved function. `ParameterTypes` exposes a read-only Umka type metadata view for each argument, and `ResultType` exposes the result metadata. Each `UmkaTypeInfo` reports a broad managed kind, the nonblank native type name, the nonnegative native byte size when available, the nonnegative native item count when available, whether the type contains Umka-managed references, derived `IsScalar`, `IsAggregate`, and `IsDeferred` category flags for host-side dispatch, non-executing read capability checks through `CanReadAsValue()`, `CanReadAsScalar<T>()`, `CanReadAsStruct<T>()`, `CanReadAsFixedLayout<T>()`, and `CanReadAsArray<TElement>(length)`, and a concise diagnostic `ToString()` for logs. For static arrays, `ItemCount` is the native array length. For other kinds, treat it as low-level native metadata rather than field/member reflection.

`CanCallWith(arguments)` exposes the same argument compatibility checks used before native invocation, without executing the function. `CanReadResultAsValue()`, `CanReadResultAsScalar<T>()`, `CanReadResultAsStruct<T>()`, and `CanReadResultAsArray<TElement>(length)` expose the same result compatibility checks used by the try-style readers without executing the function. Use them when a generic dispatcher wants to choose a reader from metadata first.

Calls must provide exactly `ParameterCount` arguments. Missing or extra arguments are rejected before UmkaSharp writes to native parameter slots. If an exported Umka function declares trailing default parameters, C# callers must still pass those arguments explicitly. UmkaSharp does not expose default-value metadata or fill omitted default arguments. If an exported function declares a variadic parameter list such as `values: ..int`, Umka exposes that parameter as a dynamic array. UmkaSharp reports the metadata but does not build a dynamic-array argument from C# values.

Use the `params UmkaValue[]` overloads for normal call sites. For hot paths that reuse argument buffers, use the `ReadOnlySpan<UmkaValue>` overloads, for example `function.CallInt64(buffer.AsSpan(0, argumentCount))`.

For supported scalar parameters, UmkaSharp also validates the `UmkaValue` kind before the native call:

- signed integer parameters require `UmkaValueKind.Int`
- unsigned integer parameters require `UmkaValueKind.UInt`
- `bool` requires `UmkaValueKind.Bool`
- `real32` and `real` require `UmkaValueKind.Real`
- `str` requires `UmkaValueKind.String`
- pointer parameters require `UmkaValueKind.Pointer`
- static arrays require `UmkaValueKind.StaticArray`
- structs require `UmkaValueKind.Struct`
- `char` accepts integer values in the byte range; `UmkaValue.From(char)` rejects C# chars above `255`

The integer and real factory overloads are convenience entry points into the same broad Umka value kinds. For example, `UmkaValue.From((short)7)` and `UmkaValue.From(7L)` both create `UmkaValueKind.Int`, while `UmkaValue.From(7U)` and byte-range `UmkaValue.From('A')` create `UmkaValueKind.UInt`.

`UmkaValue` readers mirror those broad and narrow shapes for host-side validation and diagnostics. Use `AsInt64`, `AsUInt64`, and `AsDouble` for broad values, checked helpers such as `AsSByte`, `AsInt16`, `AsByte`, `AsUInt32`, `AsChar`, `AsEnum<TEnum>`, and `AsSingle` when the host expects a narrower managed type, `AsScalar<T>()` when generic host code knows the managed scalar target type, or `TryAsEnum<TEnum>(out value)`/`TryAsScalar<T>(out value)` when wrong-kind or out-of-range values are normal input. `AsStruct<T>` reads a struct value as its original managed struct type, and `AsStaticArray<TElement>` returns a defensive copy of a static-array payload. Use `TryAsStruct<T>(out value)` and `TryAsStaticArray<TElement>(out value)` when wrong value kind, wrong managed snapshot type, or managed-reference target type should return `false`.

Narrow integer, enum, `char`, and finite `real32` arguments are range-checked before calling Umka. Narrow integer, enum result helpers, and callback readers use checked managed conversions after the native call or callback slot read, so reading an Umka `int` result through `CallSByte()` or a callback argument through `GetSByte()` throws if the returned value does not fit in `sbyte`. `CallEnum<TEnum>` and `GetEnum<TEnum>` require the C# enum's signed or unsigned underlying storage to match the Umka enum storage; `TryCallEnum<TEnum>` and `TryGetEnum<TEnum>` return `false` for storage or result range mismatch while preserving validation and lifecycle errors. `CallSingle()`, `GetSingle()`, and `AsSingle()` reject finite values outside the `System.Single` range. Static array and struct arguments must match the native Umka byte size exactly. Static arrays must also match the native item count. Dynamic arrays, maps, interfaces, closures, weak pointers, fibers, and `any` are reported as unsupported call arguments. They can appear in function result metadata, but UmkaSharp does not expose managed result readers for them; incompatible result readers are rejected before execution. At the native metadata layer, Umka reports `any` through the interface type kind.

## Structured Arguments

`UmkaValue.FromStruct<T>` copies a sequential managed struct into an Umka struct parameter. `UmkaValue.FromStaticArray<TElement>` snapshots a managed array, `params` list, `Span<TElement>`, or `ReadOnlySpan<TElement>` and copies it into an Umka static array parameter. Static arrays can contain scalar elements or fixed-layout struct elements when the managed element layout exactly matches the Umka element layout:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct IntPair
{
    public long X;
    public long Y;
}

var sum = runtime.GetFunction("sumPair");
var result = sum.CallInt64(UmkaValue.FromStruct(new IntPair { X = 19, Y = 23 }));
```

The managed type must not contain managed references, and the Umka aggregate type must not contain Umka-managed references such as strings, dynamic arrays, maps, interfaces, closures, weak pointers, fibers, or pointer-bearing nested aggregates. Use primitive fields whose sizes match Umka exactly, for example `long` for Umka `int`, `ulong` for Umka `uint`, and `double` for Umka `real`.

## Host Object Handles

`UmkaRuntime.CreateHostHandle(target)` stores a managed object in a runtime-owned handle and exposes an opaque address that can travel through Umka pointer parameters:

```csharp
using var handle = runtime.CreateHostHandle(new ScoreRules(7));
var value = UmkaValue.FromHostHandle(handle);
```

Inside callbacks, read the handle with `GetHostObject<T>`:

```csharp
runtime.Register("bonus", frame =>
{
    var rules = frame.GetHostObject<ScoreRules>(0);
    return UmkaValue.From(rules.Bonus);
});
```

Host handles are not Umka heap values. The runtime owns the `GCHandle`, disposes remaining handles when the runtime is disposed, and rejects null, unknown, disposed, or wrong-type handles during strict callback resolution. Creating new handles requires a live, undisposed runtime that is not in diagnostic-only state after a native compile failure. If compilation fails or the native runtime terminates after a runtime error, existing handles remain managed resources that can still be read, converted, resolved, or disposed on the runtime's owning thread until runtime disposal. Use `UmkaHostHandle.IsDisposed` when host code needs to inspect handle lifecycle state without reading the target or address. Use `TryGetTarget<T>` for non-throwing direct target type checks, and use `TryGetHostObject<T>` on `UmkaRuntime` or `UmkaCallFrame` when handle absence or mismatch should be handled as data.

Umka's native `umkaAllocData(size, onFree)` API can allocate Umka-owned data with an `onFree` hook, but UmkaSharp host handles do not use that path. Treat host handles as managed-runtime-owned opaque pointers, not Umka-owned objects with Umka-side finalizers.

If Umka returns a pointer that is known to be one of the runtime-owned host handles, read it directly from the function or resolve it on the owning runtime:

```csharp
var rules = runtime.GetFunction("selectRules").CallHostObject<ScoreRules>();

if (runtime.GetFunction("selectRules").TryCallHostObject<ScoreRules>(out var optionalRules))
{
    Console.WriteLine(optionalRules.Bonus);
}

var pointer = runtime.GetFunction("selectRules").CallPointer();
if (runtime.TryGetHostObject<ScoreRules>(pointer, out var sameRules))
{
    Console.WriteLine(sameRules.Bonus);
}
```

`GetHostObject<T>` throws for null, unknown, disposed, or wrong-type handles. `TryGetHostObject<T>` returns `false` for those handle-resolution failures while preserving thread-affinity checks and pointer-parameter validation. `TryCallHostObject<T>` applies the same try-style host-handle resolution to pointer results returned by a successful function call; argument validation, result-kind validation, and Umka execution errors still throw. On a direct `UmkaHostHandle`, `TryGetTarget<T>` returns `false` only for type mismatch; disposed handles and wrong-thread access still throw.

Result readers validate the resolved result type before calling Umka. Static array and struct results need an explicit native result buffer. Use `CallArray<TElement>(length)` for static arrays when the element type maps directly to the Umka element layout, or `CallStruct<T>()` for fixed-layout structs and more specialized static-array layouts. Managed result target types and Umka aggregate result types must not contain managed references.

## Callback Type Metadata

`UmkaCallFrame.ParameterCount` exposes the number of explicit callback arguments supplied by Umka. `ParameterTypes` exposes a read-only Umka type metadata view for each callback argument, and `ResultType` exposes the expected callback result metadata. As with function metadata, callback `UmkaTypeInfo` values include the broad kind, native type name, native byte size when available, native item count when available, whether the Umka type contains managed references, and category flags.

Callback readers validate the argument index and resolved Umka type before reading a native slot. Scalar callback readers include broad readers such as `GetInt64`, `GetUInt64`, and `GetDouble`, checked narrow helpers such as `GetSByte`, `GetUInt16`, `GetChar`, `GetEnum<TEnum>`, and `GetSingle`, dynamic supported value reads through `GetValue()` and `TryGetValue()`, strict generic dispatch through `GetScalar<T>()`, and try-style generic dispatch through `TryGetEnum<TEnum>(index, out value)` and `TryGetScalar<T>(index, out value)`. `CanReadArgumentAsValue`, `CanReadArgumentAsScalar<T>`, `CanReadArgumentAsStruct<T>`, and `CanReadArgumentAsArray<TElement>` preflight callback argument metadata before choosing a strict reader; they preserve frame lifecycle and index errors, but do not read native values to prove range-sensitive conversions. `TryGetValue` returns `false` for unsupported callback argument kinds, and `TryGetEnum<TEnum>`/`TryGetScalar<T>` return `false` for unsupported target types, wrong Umka kinds, and range failures, while stale frames and invalid indexes still throw. `GetStruct<T>` and `GetArray<TElement>` use the same fixed-layout, exact-size aggregate rules as C# to Umka arguments, and reject aggregate parameters that contain Umka-managed references. Callback metadata can expose deferred Umka value kinds such as dynamic arrays, maps, interfaces, `any`, closures, weak pointers, and fibers, but typed callback readers reject those arguments until UmkaSharp has safe managed wrappers for them. Deferred callback result kinds are likewise exposed through `ResultType` and rejected before writing a managed result. Callback return values are validated before writing to Umka; use `CanReturn(value)` inside the active callback when generic code needs the same result compatibility check without committing the value. Fixed-layout static array and struct callback results are supported when the managed size and array length match Umka exactly and the Umka type has no managed-reference fields. Mismatched callback result kinds, reference-bearing aggregate result types, and out-of-range narrow integer, `char`, or finite `real32` results become managed callback failures and are available through `UmkaCallback.LastException`.

## Structured Results

`CallStruct<T>` copies an Umka by-value structured result into unmanaged storage and then marshals it into a sequential managed struct. The managed type must not contain managed references, the Umka result type must not contain Umka-managed references, and the managed marshalled size must exactly match the native Umka result size.

Use sequential structs whose field order and primitive field sizes match the Umka type:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct RealPair
{
    public double X;
    public double Y;
}
```

Nested sequential structs are supported when their layout exactly matches the Umka result layout. Static arrays can be represented by fixed-layout structs when the field layout is equivalent and the managed byte size matches the native array size.

Static array results can also be copied into managed arrays:

```csharp
long[] values = runtime.GetFunction("ints").CallArray<long>(3);
```

`CallArray<TElement>` requires unmanaged element types, a requested length that matches the Umka static-array item count, and an element size that exactly matches the native Umka result size. A mismatch is rejected before the Umka function is called.

`TElement` may itself be a sequential fixed-layout struct when reading `[N]SomeStruct` results, subject to the same no-managed-references and exact-size rules.

Umka multiple-return values are represented by the native runtime as a structured result. For fixed-layout result lists, read them with `CallStruct<T>()`:

```umka
fn split*(): (int, int) {
    return 19, 23
}
```

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct IntPair
{
    public long X;
    public long Y;
}

var split = runtime.GetFunction("split").CallStruct<IntPair>();
```

This is layout-based marshalling, not a managed tuple wrapper. The same no-managed-references and exact-size rules apply.

## Unsupported Or Deliberately Deferred

These Umka features are not exposed as first-class managed wrappers:

- Dynamic arrays
- Maps
- Interfaces
- Closures
- Weak pointers
- Fibers
- `any`
- Reading dynamic arrays, maps, interfaces, closures, weak pointers, fibers, or `any` as managed results
- Umka-side lifetime callbacks for managed host handles
- Calling exported variadic functions from C# with expanded `params` arguments
- Dedicated Umka enum metadata/member-name wrappers
- Passing aggregate arguments that contain Umka-managed references
- Reading aggregate function results that contain Umka-managed references
- Returning dynamic arrays, maps, interfaces, closures, weak pointers, fibers, or `any` from C# callbacks
- Returning aggregate values from C# callbacks when the Umka result type contains managed references

Model data crossing the managed/native boundary with scalars, strings, pointers, and carefully matched fixed-layout aggregates.
