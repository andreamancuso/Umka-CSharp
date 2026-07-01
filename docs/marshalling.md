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
| weak pointer types such as `weak ^int` | `UmkaValue.FromWeakPointer(ulong)` | `CallWeakPointer`, `TryCallWeakPointer`, or `CallValue` | `GetWeakPointer`, `TryGetWeakPointer`, or `GetValue` / `UmkaValue.FromWeakPointer(ulong)` |
| built-in `any` | `UmkaAnyValue.Null.ToValue()`, scalar/string `UmkaAnyValue.From(...).ToValue()`, or `UmkaAnyValue.From(UmkaNativeValue).ToValue()` | `CallAny`, `TryCallAny`, or `CallValue` | `GetAny`, `TryGetAny`, or `GetValue` / `UmkaAnyValue...ToValue()` |
| declared non-empty interfaces | retained concrete Umka structs or exact retained interface values passed back to the same runtime | retain with `CallNativeValue()` and pass unchanged; managed interface dispatch is not exposed | `GetNativeValue(index)` for callback arguments; callback interface results can return an exact retained interface value or retained concrete Umka struct |
| direct `fn` and closures | exact retained `UmkaNativeValue` passed back to the same runtime and equivalent type | `CallNativeValue()` followed by `AsCallable()` for invocation | `GetNativeValue(index)` followed by `AsCallable()` for invocation inside or after the callback, while the runtime remains alive |
| static arrays | `UmkaValue.FromStaticArray<TElement>(...)` | `CallArray<TElement>(length)`, `TryCallArray<TElement>(length, out value)`, `CallStruct<T>`, or `TryCallStruct<T>` | `GetArray<TElement>(length)` or `TryGetArray<TElement>(index, length, out value)` / `UmkaValue.FromStaticArray<TElement>(...)` |
| dynamic arrays | `UmkaValue.FromDynamicArray<TElement>(...)` when the Umka element type has no managed references | `CallDynamicArray<TElement>` or `TryCallDynamicArray<TElement>` | `GetDynamicArray<TElement>` or `TryGetDynamicArray<TElement>(index, out value)` / `UmkaValue.FromDynamicArray<TElement>(...)` |
| `[]str` dynamic arrays | `UmkaValue.FromDynamicArray(string?[])` | `CallStringArray` or `TryCallStringArray` | `GetStringArray` or `TryGetStringArray(index, out value)` / `UmkaValue.FromDynamicArray(string?[])` |
| nested dynamic arrays such as `[][]int` and `[][]str` | `UmkaValue.FromNestedDynamicArray<TElement>` when the inner element type has no Umka-managed references, or `UmkaValue.FromNestedDynamicArray(string?[][])` for `[][]str` | `CallNestedDynamicArray<TElement>` / `TryCallNestedDynamicArray<TElement>` when the inner element type has no Umka-managed references, or `CallNestedStringArray` / `TryCallNestedStringArray` for `[][]str` | `GetNestedDynamicArray<TElement>` / `TryGetNestedDynamicArray<TElement>(index, out value)` for callback arguments, or `GetNestedStringArray` / `TryGetNestedStringArray(index, out value)` for `[][]str`; callback nested-array results use `UmkaValue.FromNestedDynamicArray(...)` |
| maps | `UmkaValue.FromMap<TKey, TValue>`, `FromStringKeyMap<TValue>`, `FromStringValueMap<TKey>`, or `FromStringMap` for fixed-layout and direct-`str` key/value types | `CallMap<TKey, TValue>` / `TryCallMap<TKey, TValue>` for fixed-layout key/value types, `CallStringKeyMap<TValue>`, `CallStringValueMap<TKey>`, or `CallStringMap` for direct `str` keys or values, `CallDynamicArrayValueMap<TKey, TElement>` or `CallStringKeyDynamicArrayValueMap<TElement>` for dynamic-array values with reference-free elements, and `CallStringArrayValueMap<TKey>` or `CallStringKeyStringArrayValueMap` for `[]str` values | `GetMap<TKey, TValue>`, string-map readers, `GetDynamicArrayValueMap<TKey, TElement>`, `GetStringKeyDynamicArrayValueMap<TElement>`, `GetStringArrayValueMap<TKey>`, or `GetStringKeyStringArrayValueMap` for callback arguments; callback map results use the same `UmkaValue.FromMap...` factories as C# function arguments |
| structs | `UmkaValue.FromStruct<T>` | `CallStruct<T>` or `TryCallStruct<T>` | `GetStruct<T>` or `TryGetStruct<T>` / `UmkaValue.FromStruct<T>` |
| multiple returns such as `(int, int)` | n/a | `CallStruct<T>` or `TryCallStruct<T>` with a matching sequential struct | n/a |

`UmkaValue.FromScalar<T>()` is a convenience wrapper over the explicit scalar value factories. Use it when generic host code knows the managed scalar input type but should not choose a specific `From(...)` overload itself. `TryFromScalar<T>(value, out result)` is the try-style version for unsupported input types, unsupported null inputs, embedded-NUL strings, and out-of-range C# `char` values.

Fixed-layout aggregate inputs use `FromStruct<T>()`, `TryFromStruct<T>(value, out result)`, `FromStaticArray<TElement>()`, `TryFromStaticArray<TElement>(values, out result)`, `FromDynamicArray<TElement>()`, `TryFromDynamicArray<TElement>(values, out result)`, `FromNestedDynamicArray<TElement>()`, and `TryFromNestedDynamicArray<TElement>(values, out result)`. `[]str` inputs use the string overloads of `FromDynamicArray(...)` and `TryFromDynamicArray(...)`; `[][]str` inputs use the string overloads of `FromNestedDynamicArray(...)` and `TryFromNestedDynamicArray(...)`. Map inputs use `FromMap<TKey, TValue>()`, `TryFromMap<TKey, TValue>(values, out result)`, `FromStringKeyMap<TValue>()`, `TryFromStringKeyMap<TValue>(values, out result)`, `FromStringValueMap<TKey>()`, `TryFromStringValueMap<TKey>(values, out result)`, `FromStringMap()`, and `TryFromStringMap(values, out result)` for fixed-layout and direct-`str` key/value types. The try-style factories return `false` for null array/map inputs, null nested-array rows, managed-reference payload types, embedded-NUL strings, duplicate null string keys, or layout size overflow.

`UmkaValue.AsScalar<T>()` is the matching strict convenience wrapper over the explicit scalar value readers, while `TryAsScalar<T>(out value)` returns `false` for unsupported target types, wrong value kinds, or range failures. Explicit enum reads also have `TryAsEnum<TEnum>(out value)`, `TryCallEnum<TEnum>(out value)`, and `TryGetEnum<TEnum>(index, out value)` helpers. `CallScalar<T>()` and `TryCallScalar<T>(out value)` provide strict and try-style generic dispatch over explicit scalar function result readers.

`CallValue()` and `TryCallValue(out value)` read supported scalar, string, pointer, weak pointer, `any`, and void results into an `UmkaValue` for generic host-side dispatch. Dynamic-array and map results stay explicit because the caller must choose `TElement`, string-array, nested-array, `TKey`/`TValue`, string-map, or dynamic-array map-value readers. `TryCallVoid()` invokes functions with void or scalar results and returns `false` for structured, dynamic-array, map, or `any` result metadata; argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. Built-in `any` results use `CallAny()` or `TryCallAny(...)` when the host wants the concrete `UmkaAnyValue`. Fixed-layout struct and static-array results stay explicit through `CallStruct<T>()`, `TryCallStruct<T>(out value)`, `CallArray<TElement>(length)`, and `TryCallArray<TElement>(length, out value)`. Dynamic-array results use `CallDynamicArray<TElement>()`/`TryCallDynamicArray<TElement>(out value)` for reference-free elements, `CallStringArray()`/`TryCallStringArray(out value)` for `[]str`, `CallNestedDynamicArray<TElement>()`/`TryCallNestedDynamicArray<TElement>(out value)` for nested arrays whose inner element type has no Umka-managed references, and `CallNestedStringArray()`/`TryCallNestedStringArray(out value)` for `[][]str`. Fixed-layout map results use `CallMap<TKey, TValue>()` or `TryCallMap<TKey, TValue>(out value)`; direct string key/value maps use `CallStringKeyMap<TValue>()`, `CallStringValueMap<TKey>()`, `CallStringMap()`, or their try-style counterparts; map results with dynamic-array values use `CallDynamicArrayValueMap<TKey, TElement>()` or `CallStringKeyDynamicArrayValueMap<TElement>()` when the array element type contains no Umka-managed references, and `CallStringArrayValueMap<TKey>()` or `CallStringKeyStringArrayValueMap()` when the value type is `[]str`. Weak pointer results use `CallWeakPointer()` or `TryCallWeakPointer(out value)`.

`GetValue()` and `TryGetValue(out value)` read supported callback scalar, string, pointer, weak pointer, and built-in `any` arguments into an `UmkaValue`; `TryGetValue` returns `false` for unsupported callback argument metadata while frame lifecycle and index errors still throw. `GetAny(index)` and `TryGetAny(index, out value)` read built-in `any` arguments as `UmkaAnyValue`. `GetScalar<T>()` and `TryGetScalar<T>(index, out value)` provide strict and try-style generic dispatch for callback arguments when generic host code knows the managed scalar type but does not need to name the specific reader. Callback aggregate arguments use `GetStruct<T>()`, `TryGetStruct<T>(index, out value)`, `GetArray<TElement>(index, length)`, `TryGetArray<TElement>(index, length, out value)`, `GetDynamicArray<TElement>(index)`, `TryGetDynamicArray<TElement>(index, out value)`, `GetStringArray(index)`, `TryGetStringArray(index, out value)`, `GetNestedDynamicArray<TElement>(index)`, `TryGetNestedDynamicArray<TElement>(index, out value)`, `GetNestedStringArray(index)`, `TryGetNestedStringArray(index, out value)`, `GetMap<TKey, TValue>(index)`, `TryGetMap<TKey, TValue>(index, out value)`, `GetStringKeyMap<TValue>(index)`, `GetStringValueMap<TKey>(index)`, `GetStringMap(index)`, `GetDynamicArrayValueMap<TKey, TElement>(index)`, `TryGetDynamicArrayValueMap<TKey, TElement>(index, out value)`, `GetStringKeyDynamicArrayValueMap<TElement>(index)`, `TryGetStringKeyDynamicArrayValueMap<TElement>(index, out value)`, `GetStringArrayValueMap<TKey>(index)`, `TryGetStringArrayValueMap<TKey>(index, out value)`, `GetStringKeyStringArrayValueMap(index)`, and `TryGetStringKeyStringArrayValueMap(index, out value)`. Weak pointer callback arguments use `GetWeakPointer(index)` or `TryGetWeakPointer(index, out value)`. The try-style structured, dynamic-array, string-array, nested-array, and map readers return `false` for incompatible metadata, unsupported reference-bearing element/key/value or aggregate types, wrong static-array length, or mismatched managed layout while argument validation, lifecycle, thread-affinity, and Umka execution errors still throw. Direct `str` values are special-cased as `[]str` elements, `[][]str` inner elements, direct map keys/values, and `[]str` map-value elements; other dynamic-array map values are copied only when their element type has no Umka-managed references.

Built-in `any` values are represented by `UmkaAnyValue`. `PayloadType` reports the concrete Umka payload metadata and `Payload` contains either a copied scalar/string value or a retained `UmkaNativeValue` for heap payloads such as dynamic arrays, maps, fixed arrays, structs, direct `fn`, and closures. Use `UmkaAnyValue.Null` for null `any`, scalar/string `UmkaAnyValue.From(...)` overloads for copied payloads, or `UmkaAnyValue.From(UmkaNativeValue)` to box an exact retained payload from the same runtime. Managed aggregate snapshots such as `UmkaValue.FromMap(...)`, `FromDynamicArray(...)`, or `FromStruct(...)` cannot be boxed into `any` directly because they do not carry the concrete native `Type *` metadata required by Umka.

Declared non-empty interfaces and closure values can still be retained as opaque `UmkaNativeValue` handles and passed back unchanged to the same runtime and equivalent Umka type. UmkaSharp exposes built-in `any` metadata as `UmkaTypeInfo.IsAny`; declared non-empty interfaces remain `UmkaTypeKind.Interface` metadata with method-table details not exposed. Retained concrete Umka structs can be assigned to non-empty interface parameters or callback results when Umka can construct the native method table. Retained direct `fn` and closure values expose `IsCallable` and can be converted to an `UmkaFunction` with `AsCallable()` or `TryAsCallable(out function)`, then invoked through the normal function `Call*` APIs. UmkaSharp does not expose interface method tables, perform runtime type assertions, or dispatch interface methods from C#. Fiber values remain metadata-only because Umka stores them as internal `Fiber *` VM execution state and exposes fiber creation/resume through language builtins rather than public host C API functions. Long-lived map entry wrappers and maps containing unsupported heap/reference values remain unsupported.

Strings are marshalled as UTF-8. A `null` C# string is passed as a null Umka string pointer and a null Umka string result is returned as `null`. Embedded NUL characters are rejected before crossing the native string boundary.

Umka enum types are reported as their underlying integer kind. For example, `enum` uses signed integer marshalling, while `enum(uint8)` uses unsigned byte-range marshalling. `FromEnum<TEnum>`, `AsEnum<TEnum>`/`TryAsEnum<TEnum>`, `CallEnum<TEnum>`/`TryCallEnum<TEnum>`, and `GetEnum<TEnum>`/`TryGetEnum<TEnum>` use the C# enum's underlying signed or unsigned storage. `UmkaTypeInfo.IsEnum` identifies enum metadata, and `UmkaTypeInfo.EnumMembers` exposes each declared member name with signed and unsigned views of the same native value. UmkaSharp does not validate that an integer value passed as an enum corresponds to a declared Umka enum constant.

Pointer values are opaque. `UmkaValue.FromPointer(IntPtr)` passes raw pointer-sized values and UmkaSharp does not own or validate the memory behind them. For managed host objects, prefer `UmkaRuntime.CreateHostHandle(target)` plus `UmkaValue.FromHostHandle(handle)` and `UmkaCallFrame.GetHostObject<T>(index)`, which validate that the pointer belongs to the current runtime. Use `TryGetHostObject<T>` when a null, unknown, disposed, or wrong-type handle is normal input and should return `false` instead of throwing.

Weak pointer values are opaque 64-bit Umka handles. Use `UmkaValue.FromWeakPointer(handle)` to pass one back to Umka, `CallWeakPointer()` to read a function result, and `GetWeakPointer(index)` inside callbacks. UmkaSharp does not strengthen weak pointers, validate that the target is still live, root the target, or own the target lifetime.

## Function Type Metadata

`UmkaFunction.ParameterCount` exposes the total number of explicit Umka parameters expected by a resolved function. `RequiredParameterCount` exposes the minimum C# argument count after trailing supported defaults are considered, and `DefaultParameterCount` exposes the number of trailing Umka parameters that declare defaults. `ParameterTypes` exposes a read-only Umka type metadata view for each argument, and `ResultType` exposes the result metadata. Each `UmkaTypeInfo` reports a broad managed kind, the nonblank native type name, the nonnegative native byte size when available, the nonnegative native item count when available, whether the type contains Umka-managed references, array-like element metadata through `ElementKind`, `ElementTypeName`, `ElementNativeSize`, and `ElementHasReferences`, nested dynamic-array inner element metadata through `NestedElementKind`, `NestedElementTypeName`, `NestedElementNativeSize`, and `NestedElementHasReferences`, map key/value metadata through `MapKeyKind`, `MapKeyTypeName`, `MapKeyNativeSize`, `MapKeyHasReferences`, `MapValueKind`, `MapValueTypeName`, `MapValueNativeSize`, and `MapValueHasReferences`, map dynamic-array value element metadata through `MapValueElementKind`, `MapValueElementTypeName`, `MapValueElementNativeSize`, and `MapValueElementHasReferences`, enum marker/member metadata when the type is an enum, `IsVariadicParameterList` for Umka variadic dynamic-array parameters, derived `IsScalar`, `IsAggregate`, `IsCallable`, and `IsDeferred` category flags for host-side dispatch, non-executing read capability checks through `CanReadAsValue()`, `CanRetainAsNativeValue()`, `CanReadAsScalar<T>()`, `CanReadAsWeakPointer()`, `CanReadAsStruct<T>()`, `CanReadAsFixedLayout<T>()`, `CanReadAsArray<TElement>(length)`, `CanReadAsDynamicArray<TElement>()`, `CanReadAsStringArray()`, `CanReadAsNestedDynamicArray<TElement>()`, `CanReadAsNestedStringArray()`, `CanReadAsMap<TKey, TValue>()`, `CanReadAsDynamicArrayValueMap<TKey, TElement>()`, and `CanReadAsStringKeyDynamicArrayValueMap<TElement>()`, and a concise diagnostic `ToString()` for logs. For static arrays, `ItemCount` is the native array length. For dynamic arrays, element metadata controls copy-based support. For nested dynamic arrays, nested element metadata controls supported readers and managed construction. For maps, key/value metadata controls copy-based support, and map value element metadata controls dynamic-array value copy-out support. For enum types, `EnumMembers` is the enum member list. For other kinds, treat `ItemCount` as low-level native metadata rather than field/member reflection.

`CanCallWith(arguments)` exposes the same argument compatibility checks used before native invocation, without executing the function. `CanReadResultAsValue()`, `CanReadResultAsScalar<T>()`, `CanReadResultAsWeakPointer()`, `CanReadResultAsStruct<T>()`, `CanReadResultAsArray<TElement>(length)`, `CanReadResultAsDynamicArray<TElement>()`, `CanReadResultAsStringArray()`, `CanReadResultAsNestedDynamicArray<TElement>()`, `CanReadResultAsMap<TKey, TValue>()`, `CanReadResultAsDynamicArrayValueMap<TKey, TElement>()`, and `CanReadResultAsStringKeyDynamicArrayValueMap<TElement>()` expose the same result compatibility checks used by the try-style readers without executing the function. Use them when a generic dispatcher wants to choose a reader from metadata first.

Calls must provide between `RequiredParameterCount` and `ParameterCount` arguments unless the final parameter is variadic; variadic calls may provide any number of trailing values after the fixed parameters. Missing required arguments and extra non-variadic arguments are rejected before UmkaSharp writes to native parameter slots. Omitted trailing Umka defaults are supported when the default parameter type is scalar, string, or pointer. Omitted defaults for dynamic arrays, maps, interfaces, closures, fibers, `any`, and reference-bearing aggregates are rejected because omitted heap/reference defaults require ownership rules that UmkaSharp does not expose. Current Umka source cannot declare weak pointer default parameters: `null` and weak pointer casts are rejected in default expressions because conversion to a weak pointer is not allowed in constant expressions. If an exported function declares a variadic parameter list such as `values: ..int`, Umka exposes that parameter as a dynamic array with `IsVariadicParameterList == true`. Pass either one explicit `UmkaValue.FromDynamicArray<TElement>(...)` value or expanded trailing `UmkaValue` arguments. Expanded values are packed into a temporary dynamic array when the variadic element kind is a supported scalar, pointer, fixed-layout struct, or fixed-layout static array without Umka-managed references. For `..str`, pass one explicit `UmkaValue.FromDynamicArray(string?[])`; expanded C# string arguments are not packed into a variadic `[]str` yet. Variadic lists whose element type contains other Umka-managed references remain rejected for the same reason as other reference-bearing dynamic arrays.

Use the `params UmkaValue[]` overloads for normal call sites. For hot paths that reuse argument buffers, use the `ReadOnlySpan<UmkaValue>` overloads, for example `function.CallInt64(buffer.AsSpan(0, argumentCount))`.

For supported scalar parameters, UmkaSharp also validates the `UmkaValue` kind before the native call:

- signed integer parameters require `UmkaValueKind.Int`
- unsigned integer parameters require `UmkaValueKind.UInt`
- `bool` requires `UmkaValueKind.Bool`
- `real32` and `real` require `UmkaValueKind.Real`
- `str` requires `UmkaValueKind.String`
- pointer parameters require `UmkaValueKind.Pointer`
- weak pointer parameters require `UmkaValueKind.WeakPointer`
- static arrays require `UmkaValueKind.StaticArray`
- dynamic arrays require `UmkaValueKind.DynamicArray`
- structs require `UmkaValueKind.Struct`
- `char` accepts integer values in the byte range; `UmkaValue.From(char)` rejects C# chars above `255`

The integer and real factory overloads are convenience entry points into the same broad Umka value kinds. For example, `UmkaValue.From((short)7)` and `UmkaValue.From(7L)` both create `UmkaValueKind.Int`, while `UmkaValue.From(7U)` and byte-range `UmkaValue.From('A')` create `UmkaValueKind.UInt`.

`UmkaValue` readers mirror those broad and narrow shapes for host-side validation and diagnostics. Use `AsInt64`, `AsUInt64`, and `AsDouble` for broad values, checked helpers such as `AsSByte`, `AsInt16`, `AsByte`, `AsUInt32`, `AsChar`, `AsEnum<TEnum>`, and `AsSingle` when the host expects a narrower managed type, `AsScalar<T>()` when generic host code knows the managed scalar target type, or `TryAsEnum<TEnum>(out value)`/`TryAsScalar<T>(out value)` when wrong-kind or out-of-range values are normal input. `AsStruct<T>` reads a struct value as its original managed struct type, and `AsStaticArray<TElement>` returns a defensive copy of a static-array payload. Use `TryAsStruct<T>(out value)` and `TryAsStaticArray<TElement>(out value)` when wrong value kind, wrong managed snapshot type, or managed-reference target type should return `false`.

Narrow integer, enum, `char`, and finite `real32` arguments are range-checked before calling Umka. Narrow integer, enum result helpers, and callback readers use checked managed conversions after the native call or callback slot read, so reading an Umka `int` result through `CallSByte()` or a callback argument through `GetSByte()` throws if the returned value does not fit in `sbyte`. `CallEnum<TEnum>` and `GetEnum<TEnum>` require the C# enum's signed or unsigned underlying storage to match the Umka enum storage; `TryCallEnum<TEnum>` and `TryGetEnum<TEnum>` return `false` for storage or result range mismatch while preserving validation and lifecycle errors. `CallSingle()`, `GetSingle()`, and `AsSingle()` reject finite values outside the `System.Single` range. Static array and struct arguments must match the native Umka byte size exactly. Static arrays must also match the native item count. Dynamic arrays must either use an unmanaged managed element type whose marshalled size exactly matches a reference-free Umka element size, use the `[]str` string-array APIs, or be passed back as an exact retained native value. Built-in `any` arguments can be constructed from null, scalar/string values, or exact retained native payloads owned by the same runtime; retained heap payloads must match the concrete Umka type. Declared non-empty interface arguments can be passed as exact retained interface values or retained concrete Umka structs when Umka can construct the interface method table; closure arguments can be passed only as retained native values owned by the same runtime and equivalent Umka type. Fiber arguments are rejected because the value is an internal VM `Fiber *` with no public host resume/ownership API. Fixed-layout maps can appear in function result metadata and can be read with `CallMap<TKey, TValue>()` when managed key/value sizes match native key/value sizes. Maps with direct `str` keys or values can be read with `CallStringKeyMap<TValue>()`, `CallStringValueMap<TKey>()`, or `CallStringMap()`. Maps with dynamic-array values can be read with the map-value readers when the key is fixed-layout or direct `str` and the value element type is reference-free or direct `str`. Maps containing Umka-managed references other than direct `str` or supported dynamic-array values with reference-free or direct-`str` elements are rejected by copy readers but may be retainable as opaque native values when the fork API supports the concrete type. At the native metadata layer, Umka reports built-in `any` through the interface type kind; `UmkaTypeInfo.IsAny` distinguishes that shape from declared non-empty interfaces.

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

The managed type must not contain managed references, and the Umka aggregate type must not contain Umka-managed references such as strings, dynamic arrays, maps, interfaces, closures, fibers, or pointer-bearing nested aggregates. Use primitive fields whose sizes match Umka exactly, for example `long` for Umka `int`, `ulong` for Umka `uint`, `double` for Umka `real`, and `ulong` for weak pointer fields.

Static arrays whose element type is an Umka weak pointer, such as `[2]weak ^int`, are copied as `ulong[]` opaque handle arrays. Struct fields of type `weak ^T` are copied as `ulong` fields. UmkaSharp copies the 64-bit handles and does not strengthen, validate, root, or own the target.

## Dynamic Arrays

Dynamic arrays are copied across the boundary. `UmkaValue.FromDynamicArray<TElement>()` snapshots a managed array, `params` list, `Span<TElement>`, or `ReadOnlySpan<TElement>` and writes a fresh Umka dynamic array for a function argument or callback result. `CallDynamicArray<TElement>()` and `GetDynamicArray<TElement>()` copy Umka dynamic-array contents into a managed array. The managed element type must not contain managed references, its marshalled size must match `UmkaTypeInfo.ElementNativeSize`, and `ElementHasReferences` must be `false`.

```csharp
var sum = runtime.GetFunction("sum");
var result = sum.CallInt64(UmkaValue.FromDynamicArray(10L, 14L, 18L));

long[] values = runtime.GetFunction("values").CallDynamicArray<long>();
```

Dynamic arrays whose element type is an Umka weak pointer, such as `[]weak ^int`, are copied as `ulong[]` opaque handle arrays. The handles follow the same weak pointer rule as scalar weak pointer values: UmkaSharp copies the 64-bit handle and does not strengthen, validate, root, or own the target.

`[]str` uses a string-specific copy path. `UmkaValue.FromDynamicArray(string?[])` snapshots managed strings, rejects embedded NUL characters, creates Umka-owned `str` values inside a fresh Umka dynamic array, and accepts `null` elements as null Umka string pointers. `CallStringArray()` and `GetStringArray(index)` copy each returned Umka `str` into a managed `string?`.

```csharp
var join = runtime.GetFunction("joinText");
var text = join.CallString(UmkaValue.FromDynamicArray("um", "ka"));

string?[] values = runtime.GetFunction("textValues").CallStringArray();
```

Nested dynamic arrays such as `[][]int` are supported when the inner element type contains no Umka-managed references. Use `UmkaValue.FromNestedDynamicArray<TElement>()` for C# function arguments and callback results, `CallNestedDynamicArray<TElement>()` for function results, and `GetNestedDynamicArray<TElement>(index)` for callback arguments. `[][]str` uses string-specific paths: `UmkaValue.FromNestedDynamicArray(string?[][])`, `CallNestedStringArray()`, and `GetNestedStringArray(index)`. Values are copied immediately across the boundary; UmkaSharp does not expose long-lived row handles.

```csharp
long[][] matrix = runtime.GetFunction("matrix").CallNestedDynamicArray<long>();
```

Nested-array construction allocates the outer Umka array and then each inner row inside Umka-owned heap storage. UmkaSharp supports that construction only for rows whose element values are reference-free or direct `str`; nested arrays of maps, interfaces, closures, fibers, `any`, or structs containing those references remain rejected because copying those entries would copy Umka heap references without a safe managed ownership model.

Dynamic arrays whose element type contains Umka-managed references other than direct `str` or a supported copy-out nested array are rejected by copy APIs. That includes arrays of maps, interfaces, closures, fibers, `any`, and structs or static arrays containing those values. `UmkaNativeValue` may retain a supported dynamic array as an opaque value, but UmkaSharp does not expose array element inspection or mutation through a managed wrapper.

## Maps

Maps are copied through managed dictionaries in supported directions. Use `UmkaValue.FromMap<TKey, TValue>()`, `UmkaValue.FromStringKeyMap<TValue>()`, `UmkaValue.FromStringValueMap<TKey>()`, or `UmkaValue.FromStringMap()` for C# function arguments and callback results whose map key/value types are fixed-layout values without Umka-managed references or direct `str`. Use `CallMap<TKey, TValue>()` or `TryCallMap<TKey, TValue>(out value)` for fixed-layout Umka function results, and `GetMap<TKey, TValue>(index)` or `TryGetMap<TKey, TValue>(index, out value)` for fixed-layout callback arguments. Use `CallStringKeyMap<TValue>()`, `CallStringValueMap<TKey>()`, `CallStringMap()`, `GetStringKeyMap<TValue>(index)`, `GetStringValueMap<TKey>(index)`, and `GetStringMap(index)` for maps whose key and/or value type is direct `str`.

```csharp
Dictionary<long, long> scores = runtime.GetFunction("scores").CallMap<long, long>();
```

For `CallMap<TKey, TValue>()` and `GetMap<TKey, TValue>()`, the Umka map key and value types must not contain Umka-managed references, and the managed `TKey`/`TValue` marshalled sizes must exactly match `MapKeyNativeSize` and `MapValueNativeSize`. `map[int]int` maps to `Dictionary<long, long>` because Umka `int` is 64-bit. `map[weak ^int]int` maps to `Dictionary<ulong, long>`, and `map[int]weak ^int` maps to `Dictionary<long, ulong>`; the weak pointer handles are copied without strengthening, validation, rooting, or ownership transfer. Direct `str` keys and values are copied immediately into managed strings: `map[str]int` maps to `Dictionary<string, long>`, `map[int]str` maps to `Dictionary<long, string?>`, and `map[str]str` maps to `Dictionary<string, string?>`.

Maps with dynamic-array values are copy-out only for function results and callback arguments when the map key is fixed-layout or direct `str` and the array element type contains no Umka-managed references or is direct `str`. C# construction of dynamic-array-valued maps remains rejected. `map[int][]int` maps to `Dictionary<long, long[]>`, `map[str][]int` maps to `Dictionary<string, long[]>`, `map[int][]str` maps to `Dictionary<long, string?[]>`, and `map[str][]str` maps to `Dictionary<string, string?[]>`.

Use `CallDynamicArrayValueMap<TKey, TElement>()`, `TryCallDynamicArrayValueMap<TKey, TElement>(out value)`, `CallStringKeyDynamicArrayValueMap<TElement>()`, `TryCallStringKeyDynamicArrayValueMap<TElement>(out value)`, `CallStringArrayValueMap<TKey>()`, `TryCallStringArrayValueMap<TKey>(out value)`, `CallStringKeyStringArrayValueMap()`, `TryCallStringKeyStringArrayValueMap(out value)`, `GetDynamicArrayValueMap<TKey, TElement>(index)`, `TryGetDynamicArrayValueMap<TKey, TElement>(index, out value)`, `GetStringKeyDynamicArrayValueMap<TElement>(index)`, `TryGetStringKeyDynamicArrayValueMap<TElement>(index, out value)`, `GetStringArrayValueMap<TKey>(index)`, `TryGetStringArrayValueMap<TKey>(index, out value)`, `GetStringKeyStringArrayValueMap(index)`, or `TryGetStringKeyStringArrayValueMap(index, out value)`.

Maps of interfaces, maps of closures, maps of fibers, maps of `any`, maps of maps, and maps of structs containing those values are rejected.

Map arguments and callback results are copied into fresh Umka maps through the fork-backed native shim APIs. `CallNativeValue()` and `GetNativeValue()` can retain supported map values as opaque `UmkaNativeValue` handles and pass them back to the same runtime and equivalent map type. UmkaSharp still does not expose map-entry enumeration or mutation through retained map handles, and it rejects copy paths whose keys or values would require unsupported heap ownership, rooting, or retained references.

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

Result readers validate the resolved result type before calling Umka. Static array, struct, dynamic-array, and map copy readers need an explicit native result buffer. Use `CallAny()` for built-in `any` results, `CallArray<TElement>(length)` for static arrays when the element type maps directly to the Umka element layout, `CallDynamicArray<TElement>()` for dynamic arrays with non-reference elements, `CallStringArray()` for `[]str`, `CallNestedDynamicArray<TElement>()` for nested dynamic arrays with reference-free inner elements, `CallNestedStringArray()` for `[][]str`, `CallMap<TKey, TValue>()` for maps with non-reference keys and values, the string-map readers for direct `str` keys or values, the dynamic-array map-value readers for maps whose values are dynamic arrays with reference-free elements, the string-array map-value readers for maps whose values are `[]str`, or `CallStruct<T>()` for fixed-layout structs and more specialized static-array layouts. Use `CallNativeValue()` when the host needs to retain a supported Umka result as an opaque native value instead of copying it. Managed result target types and Umka aggregate, dynamic-array element, inner dynamic-array element, map key/value type, or dynamic-array map-value element type must not contain managed references except for direct `str` copy paths and `UmkaAnyValue` retained native payloads. Weak pointer fields, static-array elements, dynamic-array elements, nested dynamic-array inner elements, map keys, map values, and dynamic-array map-value elements are copied as `ulong` opaque handles when the native layout matches.

## Callback Type Metadata

`UmkaCallFrame.ParameterCount` exposes the number of explicit callback arguments supplied by Umka. `ParameterTypes` exposes a read-only Umka type metadata view for each callback argument, and `ResultType` exposes the expected callback result metadata. As with function metadata, callback `UmkaTypeInfo` values include the broad kind, native type name, native byte size when available, native item count when available, whether the Umka type contains managed references, array element metadata, nested dynamic-array inner element metadata, map key/value metadata, map dynamic-array value element metadata, enum marker/member metadata, and category flags.

Callback readers validate the argument index and resolved Umka type before reading a native slot. Scalar callback readers include broad readers such as `GetInt64`, `GetUInt64`, and `GetDouble`, checked narrow helpers such as `GetSByte`, `GetUInt16`, `GetChar`, `GetEnum<TEnum>`, and `GetSingle`, weak pointer reads through `GetWeakPointer(index)` and `TryGetWeakPointer(index, out value)`, built-in `any` reads through `GetAny(index)` and `TryGetAny(index, out value)`, dynamic supported value reads through `GetValue()` and `TryGetValue()`, retained native values through `GetNativeValue(index)` and `TryGetNativeValue(index, out value)`, strict generic dispatch through `GetScalar<T>()`, and try-style generic dispatch through `TryGetEnum<TEnum>(index, out value)` and `TryGetScalar<T>(index, out value)`. `CanReadArgumentAsValue`, `CanReadArgumentAsAny`, `CanReadArgumentAsNativeValue`, `CanReadArgumentAsScalar<T>`, `CanReadArgumentAsWeakPointer`, `CanReadArgumentAsStruct<T>`, `CanReadArgumentAsArray<TElement>`, `CanReadArgumentAsDynamicArray<TElement>`, `CanReadArgumentAsStringArray`, `CanReadArgumentAsNestedDynamicArray<TElement>`, `CanReadArgumentAsNestedStringArray`, `CanReadArgumentAsMap<TKey, TValue>`, `CanReadArgumentAsDynamicArrayValueMap<TKey, TElement>`, `CanReadArgumentAsStringKeyDynamicArrayValueMap<TElement>`, `CanReadArgumentAsStringArrayValueMap<TKey>`, `CanReadArgumentAsStringKeyStringArrayValueMap`, and the string-map preflight helpers inspect callback argument metadata before choosing a strict reader; they preserve frame lifecycle and index errors, but do not read native values to prove range-sensitive conversions. `TryGetValue` returns `false` for unsupported callback argument kinds, and `TryGetAny`, `TryGetEnum<TEnum>`, and `TryGetScalar<T>` return `false` for unsupported target types, wrong Umka kinds, and range failures, while stale frames and invalid indexes still throw. `GetStruct<T>`, `GetArray<TElement>`, `GetDynamicArray<TElement>`, `GetStringArray`, `GetNestedDynamicArray<TElement>`, `GetNestedStringArray`, `GetMap<TKey, TValue>`, the string-map readers, and the dynamic-array map-value readers use fixed-layout or string-specific copy rules and reject unsupported aggregate, dynamic-array element, nested dynamic-array inner element, map key/value type, or dynamic-array map-value element types that contain Umka-managed references other than direct `str`, supported `[][]str`, or supported `[]str` map-value elements. Callback metadata can expose declared interfaces and closures through `GetNativeValue`; built-in `any` has the dedicated `GetAny` reader. Fiber callback arguments are metadata-only because they are internal VM `Fiber *` values with no public host resume/status/ownership API. Map callback arguments are supported through copy-out for fixed-layout keys/values, direct `str` keys/values, or dynamic-array values with reference-free or direct-`str` elements. Nested dynamic-array callback arguments and callback results are supported when the inner element type has no Umka-managed references or is direct `str`. Callback return values are validated before writing to Umka; use `CanReturn(value)` inside the active callback when generic code needs the same result compatibility check without committing the value. Fixed-layout static array, dynamic-array, `[]str`, nested dynamic-array, `[][]str`, struct, map, built-in `any`, weak pointer, and exact retained-native callback results are supported when the managed value shape matches Umka. Fiber callback result kinds are exposed through `ResultType` and rejected before writing a managed result. Mismatched callback result kinds, unsupported reference-bearing aggregate, dynamic-array element, nested dynamic-array inner element, map key/value type, dynamic-array map-value element type, unsupported `any` payload, and out-of-range narrow integer, `char`, or finite `real32` results become managed callback failures and are available through `UmkaCallback.LastException`.

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

## Deferred Marshalling Cases

| Case | Public status | Reason |
| --- | --- | --- |
| First-class map wrappers and unsupported map payload shapes | Partly supported | `UmkaNativeValue` can retain a supported map and pass it back unchanged to the same runtime and equivalent map type. UmkaSharp does not expose map entries, mutation, or unsupported reference-bearing map payloads through managed wrappers. |
| Maps with key/value references beyond direct `str` or supported dynamic-array values | Rejected by map readers | Copy-out would duplicate Umka heap references without owning, retaining, rooting, or releasing maps, interfaces, closures, fibers, `any`, nested maps, or aggregates containing those references. |
| Built-in `any` values | Supported with explicit payload limits | `UmkaAnyValue` inspects payload metadata, constructs null/scalar/string payloads, and boxes retained same-runtime native payloads. Managed aggregate snapshots cannot be boxed into `any` directly because they do not carry concrete Umka type metadata. |
| Declared non-empty interfaces | Partly supported | `UmkaNativeValue` can retain and pass back exact interface values, and retained concrete Umka structs can satisfy interface parameters/results through Umka's native method-table construction. UmkaSharp does not expose managed method-table inspection, host construction from managed data, runtime type assertions, or interface method dispatch from C#. |
| Closure and direct `fn` values as C# callable objects | Supported with retained-value lifetime | Retain the value with `CallNativeValue()` or `GetNativeValue(index)`, call `AsCallable()`, then use the returned `UmkaFunction` `Call*` methods. The retained value and owning runtime must stay alive and on the owning thread. |
| Fiber values as host-created or host-resumed objects | Metadata-only | Umka stores fibers as internal `Fiber *` VM state, and creation/resume/status are implemented by `make(fiber, closure)`, `resume`, and `valid` builtins rather than public host C API functions. |
| Declared interface, closure, or fiber function results and callback results | Partly supported for declared interfaces; callable for retained `fn`/closures; rejected for fiber | Declared interface values can be retained as opaque native values and passed back to an equivalent Umka type; retained concrete Umka structs can satisfy non-empty interface callback results when Umka can construct the method table. Retained direct `fn` and closure values can also be invoked through `AsCallable()`. Built-in `any` uses `UmkaAnyValue`; fibers remain rejected because they have no public host resume/ownership API. |
| Nested dynamic arrays whose inner element type contains references beyond direct `str` | Rejected | Copying nested arrays of maps, interfaces, closures, fibers, `any`, or aggregates containing those references would copy Umka heap references without a managed ownership model. |
| Dynamic arrays whose element type contains references beyond direct `str` or supported nested arrays | Rejected | UmkaSharp copies array payloads immediately and does not expose rooted ownership for arrays of maps, interfaces, closures, fibers, `any`, or aggregates containing those references. |
| Aggregate arguments, function results, or callback results containing Umka-managed references | Rejected | Aggregate marshalling is by-value storage copy; UmkaSharp does not own, root, retain, release, or invoke embedded heap references. |
| Umka-owned managed object wrappers through `umkaAllocData(size, onFree)` | Deferred | Current host handles are runtime-owned `GCHandle` wrappers; Umka-owned wrappers need defined finalizer ordering, handle release, thread-affinity, runtime-shutdown, callback-reentrancy, and cleanup-error behavior. |

Model data crossing the managed/native boundary with scalars, strings, pointers, weak pointer handles, carefully matched fixed-layout aggregates, reference-free dynamic arrays, `[]str`, supported nested arrays including `[][]str`, fixed-layout maps, direct-string maps, and supported dynamic-array or string-array map values where the API explicitly exposes them.
