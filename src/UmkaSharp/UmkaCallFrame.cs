namespace UmkaSharp;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Managed transient view over an active Umka external callback frame.</summary>
public readonly struct UmkaCallFrame
{
    private readonly UmkaRuntime _runtime;
    private readonly long _frameId;
    private readonly IntPtr _parameters;
    private readonly IntPtr _result;

    internal UmkaCallFrame(UmkaRuntime runtime, long frameId, IntPtr parameters, IntPtr result)
    {
        _runtime = runtime;
        _frameId = frameId;
        _parameters = parameters;
        _result = result;
    }

    /// <summary>Gets the number of explicit callback arguments supplied by Umka while the callback frame is active.</summary>
    public int ParameterCount
    {
        get
        {
            CheckActive();
            return NativeMethods.CallbackGetArgumentCount(_parameters);
        }
    }

    /// <summary>Gets a managed snapshot of the explicit callback argument types supplied by Umka while the callback frame is active.</summary>
    public IReadOnlyList<UmkaTypeInfo> ParameterTypes
    {
        get
        {
            var parameterCount = ParameterCount;
            var parameterTypes = new UmkaTypeInfo[parameterCount];
            for (var i = 0; i < parameterCount; i++)
            {
                parameterTypes[i] = GetParameterTypeInfoOrThrow(i);
            }

            return Array.AsReadOnly(parameterTypes);
        }
    }

    /// <summary>Gets a managed snapshot of the callback result type expected by Umka while the callback frame is active.</summary>
    public UmkaTypeInfo ResultType =>
        CheckActiveAndGetResultType();

    /// <summary>Returns whether the argument metadata can be read as a dynamic value for supported scalar, string, pointer, or weak pointer kinds without reading the argument.</summary>
    public bool CanReadArgumentAsValue(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        return parameterType.Kind != UmkaTypeKind.Void && parameterType.CanReadAsValue();
    }

    /// <summary>Returns whether the argument metadata can be retained as a runtime-owned native Umka value.</summary>
    public bool CanReadArgumentAsNativeValue(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        return parameterType.Kind != UmkaTypeKind.Void && parameterType.CanRetainAsNativeValue();
    }

    /// <summary>Returns whether the argument metadata can be read as an Umka <c>any</c> value.</summary>
    public bool CanReadArgumentAsAny(int index) => GetParameterTypeInfoOrThrow(index).IsAny;

    /// <summary>Returns whether the argument metadata can be read as a supported scalar, string, pointer, enum, or dynamic value without reading the argument.</summary>
    public bool CanReadArgumentAsScalar<T>(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        return parameterType.Kind != UmkaTypeKind.Void && parameterType.CanReadAsScalar<T>();
    }

    /// <summary>Returns whether the argument metadata can be read into the managed struct type without reading the argument.</summary>
    public bool CanReadArgumentAsStruct<T>(int index) where T : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStruct<T>();

    /// <summary>Returns whether the argument metadata can be read into a managed array of the given length without reading the argument.</summary>
    public bool CanReadArgumentAsArray<TElement>(int index, int length) where TElement : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsArray<TElement>(length, requireKnownItemCount: true);

    /// <summary>Returns whether the argument metadata can be copied into a managed dynamic array without reading the argument.</summary>
    public bool CanReadArgumentAsDynamicArray<TElement>(int index) where TElement : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsDynamicArray<TElement>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with dynamic-array values without reading the argument.</summary>
    public bool CanReadArgumentAsDynamicArrayValueMap<TKey, TElement>(int index)
        where TKey : struct
        where TElement : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsDynamicArrayValueMap<TKey, TElement>();

    /// <summary>Returns whether the argument metadata can be copied into a managed jagged array without reading the argument.</summary>
    public bool CanReadArgumentAsNestedDynamicArray<TElement>(int index) where TElement : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsNestedDynamicArray<TElement>();

    /// <summary>Returns whether the argument metadata can be copied into a managed jagged string array without reading the argument.</summary>
    public bool CanReadArgumentAsNestedStringArray(int index) =>
        GetParameterTypeInfoOrThrow(index).CanReadAsNestedStringArray();

    /// <summary>Returns whether the argument metadata can be copied into a managed string array without reading the argument.</summary>
    public bool CanReadArgumentAsStringArray(int index) =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringArray();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string-array values without reading the argument.</summary>
    public bool CanReadArgumentAsStringArrayValueMap<TKey>(int index) where TKey : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringArrayValueMap<TKey>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary without reading the argument.</summary>
    public bool CanReadArgumentAsMap<TKey, TValue>(int index)
        where TKey : struct
        where TValue : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsMap<TKey, TValue>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string keys without reading the argument.</summary>
    public bool CanReadArgumentAsStringKeyMap<TValue>(int index) where TValue : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringKeyMap<TValue>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string keys and dynamic-array values without reading the argument.</summary>
    public bool CanReadArgumentAsStringKeyDynamicArrayValueMap<TElement>(int index) where TElement : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringKeyDynamicArrayValueMap<TElement>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string keys and string-array values without reading the argument.</summary>
    public bool CanReadArgumentAsStringKeyStringArrayValueMap(int index) =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringKeyStringArrayValueMap();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string values without reading the argument.</summary>
    public bool CanReadArgumentAsStringValueMap<TKey>(int index) where TKey : struct =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringValueMap<TKey>();

    /// <summary>Returns whether the argument metadata can be copied into a managed dictionary with string keys and string values without reading the argument.</summary>
    public bool CanReadArgumentAsStringMap(int index) =>
        GetParameterTypeInfoOrThrow(index).CanReadAsStringMap();

    /// <summary>Returns whether the argument metadata can be read as an opaque Umka weak pointer handle without reading the argument.</summary>
    public bool CanReadArgumentAsWeakPointer(int index) =>
        GetParameterTypeInfoOrThrow(index).CanReadAsWeakPointer();

    /// <summary>Returns whether the supplied value can be returned from this callback frame without writing it.</summary>
    public bool CanReturn(UmkaValue value)
    {
        CheckActive();

        var nativeKind = NativeMethods.CallbackGetResultKind(_parameters, _result);
        if (value.Kind == UmkaValueKind.Any)
            return CanReturnAnyValue(GetResultTypeInfo(nativeKind), value.AnyValue);
        if (value.Kind == UmkaValueKind.NativeValue)
            return CanReturnNativeValue(GetResultTypeInfo(nativeKind), value.NativeValue);

        if (!IsSupportedResultKind(nativeKind) || !IsCompatibleResultKind(nativeKind, value.Kind))
            return false;

        return value.Kind switch
        {
            UmkaValueKind.StaticArray or UmkaValueKind.Struct => CanReturnStructuredValue(nativeKind, GetResultTypeInfo(nativeKind), value),
            UmkaValueKind.DynamicArray => CanReturnDynamicArrayValue(GetResultTypeInfo(nativeKind), value),
            UmkaValueKind.Map => CanReturnMapValue(GetResultTypeInfo(nativeKind), value),
            _ => IsResultInRange(nativeKind, value)
        };
    }

    /// <summary>Reads a signed integer callback argument.</summary>
    public long GetInt64(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.SignedInteger, UmkaTypeKind.Character);
        return NativeMethods.CallbackGetParamInt(_parameters, index);
    }

    /// <summary>Reads an 8-bit signed integer callback argument.</summary>
    public sbyte GetSByte(int index) => checked((sbyte)GetInt64(index));

    /// <summary>Reads a 16-bit signed integer callback argument.</summary>
    public short GetInt16(int index) => checked((short)GetInt64(index));

    /// <summary>Reads a 32-bit signed integer callback argument.</summary>
    public int GetInt32(int index) => checked((int)GetInt64(index));

    /// <summary>Reads an unsigned integer callback argument.</summary>
    public ulong GetUInt64(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.UnsignedInteger, UmkaTypeKind.Character);
        return NativeMethods.CallbackGetParamUInt(_parameters, index);
    }

    /// <summary>Reads an 8-bit unsigned integer callback argument.</summary>
    public byte GetByte(int index) => checked((byte)GetUInt64(index));

    /// <summary>Reads a 16-bit unsigned integer callback argument.</summary>
    public ushort GetUInt16(int index) => checked((ushort)GetUInt64(index));

    /// <summary>Reads a 32-bit unsigned integer callback argument.</summary>
    public uint GetUInt32(int index) => checked((uint)GetUInt64(index));

    /// <summary>Reads a character callback argument.</summary>
    public char GetChar(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.Character);

        var value = NativeMethods.CallbackGetParamUInt(_parameters, index);
        if (value > byte.MaxValue)
            throw new OverflowException($"Callback argument {index} has character value {value}, which is outside the Umka char range.");

        return (char)value;
    }

    /// <summary>Reads an enum callback argument through the enum's underlying signed or unsigned storage.</summary>
    public TEnum GetEnum<TEnum>(int index) where TEnum : struct, Enum =>
        UmkaEnumConversion.IsUnsigned<TEnum>()
            ? UmkaEnumConversion.ToEnum<TEnum>(GetUInt64(index))
            : UmkaEnumConversion.ToEnum<TEnum>(GetInt64(index));

    /// <summary>Tries to read an enum callback argument through the enum's underlying signed or unsigned storage.</summary>
    public bool TryGetEnum<TEnum>(int index, out TEnum value) where TEnum : struct, Enum
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetEnum<TEnum>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (OverflowException)
        {
        }

        value = default;
        return false;
    }

    /// <summary>Reads a callback argument as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    [return: MaybeNull]
    public T GetScalar<T>(int index)
    {
        var targetType = typeof(T);
        if (targetType == typeof(UmkaValue))
            return BoxScalar<T>(GetValue(index));
        if (targetType == typeof(sbyte))
            return BoxScalar<T>(GetSByte(index));
        if (targetType == typeof(short))
            return BoxScalar<T>(GetInt16(index));
        if (targetType == typeof(int))
            return BoxScalar<T>(GetInt32(index));
        if (targetType == typeof(long))
            return BoxScalar<T>(GetInt64(index));
        if (targetType == typeof(byte))
            return BoxScalar<T>(GetByte(index));
        if (targetType == typeof(ushort))
            return BoxScalar<T>(GetUInt16(index));
        if (targetType == typeof(uint))
            return BoxScalar<T>(GetUInt32(index));
        if (targetType == typeof(ulong))
            return BoxScalar<T>(GetUInt64(index));
        if (targetType == typeof(float))
            return BoxScalar<T>(GetSingle(index));
        if (targetType == typeof(double))
            return BoxScalar<T>(GetDouble(index));
        if (targetType == typeof(bool))
            return BoxScalar<T>(GetBoolean(index));
        if (targetType == typeof(char))
            return BoxScalar<T>(GetChar(index));
        if (targetType == typeof(string))
            return BoxScalar<T>(GetString(index));
        if (targetType == typeof(IntPtr))
            return BoxScalar<T>(GetPointer(index));
        if (targetType.IsEnum)
            return GetEnumScalar<T>(index);

        throw new NotSupportedException(
            $"GetScalar<T>() does not support argument type {targetType.FullName}. Use an explicit reader such as GetStruct<T>(), GetArray<TElement>(), GetHostObject<T>(), or TryGetHostObject<T>().");
    }

    /// <summary>Tries to read a callback argument as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool TryGetScalar<T>(int index, [MaybeNull] out T value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetScalar<T>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (OverflowException)
        {
        }

        value = default;
        return false;
    }

    /// <summary>Reads a real callback argument.</summary>
    public double GetDouble(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.Real);
        return NativeMethods.CallbackGetParamReal(_parameters, index);
    }

    /// <summary>Reads a single-precision real callback argument.</summary>
    public float GetSingle(int index) =>
        UmkaSingleConversion.ToSingleChecked(GetDouble(index), $"Callback argument {index} real value");

    /// <summary>Reads a Boolean callback argument.</summary>
    public bool GetBoolean(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.Boolean);
        return NativeMethods.CallbackGetParamInt(_parameters, index) != 0;
    }

    /// <summary>Reads a pointer callback argument.</summary>
    public IntPtr GetPointer(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.Pointer);
        return NativeMethods.CallbackGetParamPointer(_parameters, index);
    }

    /// <summary>Reads a callback argument as an opaque Umka weak pointer handle.</summary>
    public ulong GetWeakPointer(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.WeakPointer);
        return NativeMethods.CallbackGetParamUInt(_parameters, index);
    }

    /// <summary>Tries to read a callback argument as an opaque Umka weak pointer handle.</summary>
    public bool TryGetWeakPointer(int index, out ulong value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetWeakPointer(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }

        value = default;
        return false;
    }

    /// <summary>Reads a runtime-owned host object handle callback argument.</summary>
    public T GetHostObject<T>(int index) => _runtime.GetHostObject<T>(GetPointer(index));

    /// <summary>Tries to read a runtime-owned host object handle callback argument.</summary>
    public bool TryGetHostObject<T>(int index, [NotNullWhen(true)] out T? target) =>
        _runtime.TryGetHostObject(GetPointer(index), out target);

    /// <summary>Reads a string callback argument.</summary>
    public string? GetString(int index)
    {
        EnsureParameterKind(index, UmkaTypeKind.String);
        return NativeMethods.CallbackGetParamString(_parameters, index).ToManagedString();
    }

    /// <summary>Reads a fixed-layout struct callback argument.</summary>
    public T GetStruct<T>(int index) where T : struct
    {
        ValidateNoManagedReferences<T>();
        var parameterType = GetStructuredParameterTypeOrThrow(index, UmkaTypeKind.Struct);
        EnsureReadableStructuredParameter(index, parameterType);

        var nativeSize = parameterType.NativeSize;
        var managedSize = Marshal.SizeOf<T>();
        if (managedSize != nativeSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}' with native size {nativeSize} bytes, but managed type {typeof(T).FullName} is {managedSize} bytes.");
        }

        var buffer = Marshal.AllocHGlobal(nativeSize);
        try
        {
            var status = NativeMethods.CallbackGetParamData(_parameters, index, buffer, nativeSize);
            if (status != 0)
                throw new InvalidOperationException($"Callback argument {index} could not be copied as a fixed-layout struct.");

            return Marshal.PtrToStructure<T>(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Tries to read a fixed-layout struct callback argument.</summary>
    public bool TryGetStruct<T>(int index, out T value) where T : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStruct<T>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = default;
        return false;
    }

    /// <summary>Reads a fixed-layout static array callback argument.</summary>
    public TElement[] GetArray<TElement>(int index, int length) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var parameterType = GetStructuredParameterTypeOrThrow(index, UmkaTypeKind.StaticArray);
        EnsureReadableStructuredParameter(index, parameterType);

        var nativeLength = parameterType.ItemCount;
        if (length != nativeLength)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}' with {nativeLength} item(s), but {length} item(s) were requested.");
        }

        var nativeSize = parameterType.NativeSize;
        var elementSize = Marshal.SizeOf<TElement>();
        var managedSize = checked(elementSize * length);
        if (managedSize != nativeSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}' with native size {nativeSize} bytes, but managed array type {typeof(TElement).FullName}[{length}] is {managedSize} bytes.");
        }

        var buffer = Marshal.AllocHGlobal(nativeSize);
        try
        {
            var status = NativeMethods.CallbackGetParamData(_parameters, index, buffer, nativeSize);
            if (status != 0)
                throw new InvalidOperationException($"Callback argument {index} could not be copied as a fixed-layout static array.");

            var result = new TElement[length];
            for (var i = 0; i < result.Length; i++)
                result[i] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, i * elementSize));
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Tries to read a fixed-layout static array callback argument.</summary>
    public bool TryGetArray<TElement>(
        int index,
        int length,
        [NotNullWhen(true)] out TElement[]? value)
        where TElement : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetArray<TElement>(index, length);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a dynamic array callback argument into a managed array.</summary>
    public TElement[] GetDynamicArray<TElement>(int index) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.DynamicArray}.");
        }

        EnsureReadableDynamicArrayParameter<TElement>(index, parameterType);

        var length = NativeMethods.CallbackGetParamDynamicArrayLength(_parameters, index);
        if (length < 0)
            throw new InvalidOperationException($"Callback argument {index} could not be read as a dynamic array.");
        if (length == 0)
            return Array.Empty<TElement>();

        var elementSize = Marshal.SizeOf<TElement>();
        var byteSize = checked(length * elementSize);
        var buffer = Marshal.AllocHGlobal(byteSize);
        try
        {
            var status = NativeMethods.CallbackCopyParamDynamicArrayData(_parameters, index, buffer, byteSize);
            if (status != 0)
                throw new InvalidOperationException($"Callback argument {index} could not be copied as a dynamic array.");

            var result = new TElement[length];
            for (var i = 0; i < result.Length; i++)
                result[i] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, i * elementSize));
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Tries to copy a dynamic array callback argument into a managed array.</summary>
    public bool TryGetDynamicArray<TElement>(
        int index,
        [NotNullWhen(true)] out TElement[]? value)
        where TElement : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetDynamicArray<TElement>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a <c>[]str</c> dynamic array callback argument into a managed string array.</summary>
    public string?[] GetStringArray(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.DynamicArray}.");
        }

        EnsureReadableStringDynamicArrayParameter(index, parameterType);

        var length = NativeMethods.CallbackGetParamDynamicArrayLength(_parameters, index);
        if (length < 0)
            throw new InvalidOperationException($"Callback argument {index} could not be read as a dynamic array.");
        if (length == 0)
            return Array.Empty<string?>();

        var result = new string?[length];
        for (var i = 0; i < result.Length; i++)
        {
            var status = NativeMethods.CallbackGetParamDynamicArrayString(_parameters, index, i, out var value);
            if (status != 0)
                throw new InvalidOperationException($"Callback argument {index} could not be copied as a string dynamic array.");

            result[i] = value.ToManagedString();
        }

        return result;
    }

    /// <summary>Tries to copy a <c>[]str</c> dynamic array callback argument into a managed string array.</summary>
    public bool TryGetStringArray(
        int index,
        [NotNullWhen(true)] out string?[]? value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringArray(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a nested dynamic array callback argument into a managed jagged array.</summary>
    public TElement[][] GetNestedDynamicArray<TElement>(int index) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.DynamicArray}.");
        }

        EnsureReadableNestedDynamicArrayParameter<TElement>(index, parameterType);

        var length = NativeMethods.CallbackGetParamDynamicArrayLength(_parameters, index);
        if (length < 0)
            throw new InvalidOperationException($"Callback argument {index} could not be read as a dynamic array.");
        if (length == 0)
            return Array.Empty<TElement[]>();

        var elementSize = Marshal.SizeOf<TElement>();
        var result = new TElement[length][];
        for (var i = 0; i < result.Length; i++)
        {
            var rowLength = NativeMethods.CallbackGetParamNestedDynamicArrayLength(_parameters, index, i);
            if (rowLength < 0)
                throw new InvalidOperationException($"Callback argument {index} row {i} could not be read as a nested dynamic array.");
            if (rowLength == 0)
            {
                result[i] = Array.Empty<TElement>();
                continue;
            }

            var byteSize = checked(rowLength * elementSize);
            var buffer = Marshal.AllocHGlobal(byteSize);
            try
            {
                var status = NativeMethods.CallbackCopyParamNestedDynamicArrayData(_parameters, index, i, buffer, byteSize, elementSize);
                if (status != 0)
                    throw new InvalidOperationException($"Callback argument {index} row {i} could not be copied as a nested dynamic array.");

                var row = new TElement[rowLength];
                for (var j = 0; j < row.Length; j++)
                    row[j] = Marshal.PtrToStructure<TElement>(IntPtr.Add(buffer, j * elementSize));
                result[i] = row;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return result;
    }

    /// <summary>Tries to copy a nested dynamic array callback argument into a managed jagged array.</summary>
    public bool TryGetNestedDynamicArray<TElement>(
        int index,
        [NotNullWhen(true)] out TElement[][]? value)
        where TElement : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetNestedDynamicArray<TElement>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a nested <c>[][]str</c> dynamic array callback argument into a managed jagged string array.</summary>
    public string?[][] GetNestedStringArray(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.DynamicArray}.");
        }

        EnsureReadableNestedStringArrayParameter(index, parameterType);

        var length = NativeMethods.CallbackGetParamDynamicArrayLength(_parameters, index);
        if (length < 0)
            throw new InvalidOperationException($"Callback argument {index} could not be read as a dynamic array.");
        if (length == 0)
            return Array.Empty<string?[]>();

        var result = new string?[length][];
        for (var i = 0; i < result.Length; i++)
            result[i] = CopyNestedStringArrayParameterRow(index, i);

        return result;
    }

    /// <summary>Tries to copy a nested <c>[][]str</c> dynamic array callback argument into a managed jagged string array.</summary>
    public bool TryGetNestedStringArray(
        int index,
        [NotNullWhen(true)] out string?[][]? value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetNestedStringArray(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    private string?[] CopyNestedStringArrayParameterRow(int index, int rowIndex)
    {
        var rowLength = NativeMethods.CallbackGetParamNestedStringArrayLength(_parameters, index, rowIndex);
        if (rowLength < 0)
            throw new InvalidOperationException($"Callback argument {index} row {rowIndex} could not be read as a nested string array.");
        if (rowLength == 0)
            return Array.Empty<string?>();

        var byteSize = checked(rowLength * IntPtr.Size);
        var buffer = Marshal.AllocHGlobal(byteSize);
        try
        {
            var status = NativeMethods.CallbackCopyParamNestedStringArrayData(_parameters, index, rowIndex, buffer, rowLength);
            if (status != 0)
                throw new InvalidOperationException($"Callback argument {index} row {rowIndex} could not be copied as a nested string array.");

            var row = new string?[rowLength];
            for (var j = 0; j < row.Length; j++)
                row[j] = Marshal.ReadIntPtr(buffer, j * IntPtr.Size).ToManagedString();
            return row;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Copies a map callback argument into a managed dictionary.</summary>
    public Dictionary<TKey, TValue> GetMap<TKey, TValue>(int index)
        where TKey : struct
        where TValue : struct
    {
        ValidateNoManagedReferences<TKey>();
        ValidateNoManagedReferences<TValue>();
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.Map)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.Map}.");
        }

        EnsureReadableMapParameter<TKey, TValue>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.Copy<TKey, TValue>(
            count,
            parameterType.MapKeyNativeSize,
            parameterType.MapValueNativeSize,
            (keys, keyBytes, values, valueBytes) =>
                NativeMethods.CallbackCopyParamMapEntries(parameters, index, keys, keyBytes, values, valueBytes));
    }

    /// <summary>Tries to copy a map callback argument into a managed dictionary.</summary>
    public bool TryGetMap<TKey, TValue>(
        int index,
        [NotNullWhen(true)] out Dictionary<TKey, TValue>? value)
        where TKey : struct
        where TValue : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetMap<TKey, TValue>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string keys into a managed dictionary.</summary>
    public Dictionary<string, TValue> GetStringKeyMap<TValue>(int index)
        where TValue : struct
    {
        ValidateNoManagedReferences<TValue>();
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringKeyMapParameter<TValue>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStringKeys<TValue>(
            count,
            parameterType.MapValueNativeSize,
            (keys, keyCount, values, valueBytes) =>
                NativeMethods.CallbackCopyParamStringKeyMapEntries(parameters, index, keys, keyCount, values, valueBytes));
    }

    /// <summary>Tries to copy a map callback argument with string keys into a managed dictionary.</summary>
    public bool TryGetStringKeyMap<TValue>(
        int index,
        [NotNullWhen(true)] out Dictionary<string, TValue>? value)
        where TValue : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringKeyMap<TValue>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string values into a managed dictionary.</summary>
    public Dictionary<TKey, string?> GetStringValueMap<TKey>(int index)
        where TKey : struct
    {
        ValidateNoManagedReferences<TKey>();
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringValueMapParameter<TKey>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStringValues<TKey>(
            count,
            parameterType.MapKeyNativeSize,
            (keys, keyBytes, values, valueCount) =>
                NativeMethods.CallbackCopyParamStringValueMapEntries(parameters, index, keys, keyBytes, values, valueCount));
    }

    /// <summary>Tries to copy a map callback argument with string values into a managed dictionary.</summary>
    public bool TryGetStringValueMap<TKey>(
        int index,
        [NotNullWhen(true)] out Dictionary<TKey, string?>? value)
        where TKey : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringValueMap<TKey>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string keys and string values into a managed dictionary.</summary>
    public Dictionary<string, string?> GetStringMap(int index)
    {
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringMapParameter(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStrings(
            count,
            (keys, keyCount, values, valueCount) =>
                NativeMethods.CallbackCopyParamStringMapEntries(parameters, index, keys, keyCount, values, valueCount));
    }

    /// <summary>Tries to copy a map callback argument with string keys and string values into a managed dictionary.</summary>
    public bool TryGetStringMap(
        int index,
        [NotNullWhen(true)] out Dictionary<string, string?>? value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringMap(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with dynamic-array values into a managed dictionary.</summary>
    public Dictionary<TKey, TElement[]> GetDynamicArrayValueMap<TKey, TElement>(int index)
        where TKey : struct
        where TElement : struct
    {
        ValidateNoManagedReferences<TKey>();
        ValidateNoManagedReferences<TElement>();
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableDynamicArrayValueMapParameter<TKey, TElement>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyDynamicArrayValues<TKey, TElement>(
            count,
            parameterType.MapKeyNativeSize,
            parameterType.MapValueElementNativeSize,
            (keys, keyBytes, lengths, lengthCount, elementSize) =>
                NativeMethods.CallbackCopyParamMapDynamicArrayValueEntries(parameters, index, keys, keyBytes, lengths, lengthCount, elementSize),
            (entryIndex, buffer, byteSize, elementSize) =>
                NativeMethods.CallbackCopyParamMapDynamicArrayValueData(parameters, index, entryIndex, buffer, byteSize, elementSize));
    }

    /// <summary>Tries to copy a map callback argument with dynamic-array values into a managed dictionary.</summary>
    public bool TryGetDynamicArrayValueMap<TKey, TElement>(
        int index,
        [NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value)
        where TKey : struct
        where TElement : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetDynamicArrayValueMap<TKey, TElement>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string keys and dynamic-array values into a managed dictionary.</summary>
    public Dictionary<string, TElement[]> GetStringKeyDynamicArrayValueMap<TElement>(int index)
        where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringKeyDynamicArrayValueMapParameter<TElement>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStringKeyDynamicArrayValues<TElement>(
            count,
            parameterType.MapValueElementNativeSize,
            (keys, keyCount, lengths, lengthCount, elementSize) =>
                NativeMethods.CallbackCopyParamStringKeyMapDynamicArrayValueEntries(parameters, index, keys, keyCount, lengths, lengthCount, elementSize),
            (entryIndex, buffer, byteSize, elementSize) =>
                NativeMethods.CallbackCopyParamStringKeyMapDynamicArrayValueData(parameters, index, entryIndex, buffer, byteSize, elementSize));
    }

    /// <summary>Tries to copy a map callback argument with string keys and dynamic-array values into a managed dictionary.</summary>
    public bool TryGetStringKeyDynamicArrayValueMap<TElement>(
        int index,
        [NotNullWhen(true)] out Dictionary<string, TElement[]>? value)
        where TElement : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringKeyDynamicArrayValueMap<TElement>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string-array values into a managed dictionary.</summary>
    public Dictionary<TKey, string?[]> GetStringArrayValueMap<TKey>(int index)
        where TKey : struct
    {
        ValidateNoManagedReferences<TKey>();
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringArrayValueMapParameter<TKey>(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStringArrayValues<TKey>(
            count,
            parameterType.MapKeyNativeSize,
            (keys, keyBytes, lengths, lengthCount) =>
                NativeMethods.CallbackCopyParamMapStringArrayValueEntries(parameters, index, keys, keyBytes, lengths, lengthCount),
            (entryIndex, values, valueCount) =>
                NativeMethods.CallbackCopyParamMapStringArrayValueData(parameters, index, entryIndex, values, valueCount));
    }

    /// <summary>Tries to copy a map callback argument with string-array values into a managed dictionary.</summary>
    public bool TryGetStringArrayValueMap<TKey>(
        int index,
        [NotNullWhen(true)] out Dictionary<TKey, string?[]>? value)
        where TKey : struct
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringArrayValueMap<TKey>(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Copies a map callback argument with string keys and string-array values into a managed dictionary.</summary>
    public Dictionary<string, string?[]> GetStringKeyStringArrayValueMap(int index)
    {
        var parameterType = GetMapParameterTypeInfoOrThrow(index);
        EnsureReadableStringKeyStringArrayValueMapParameter(index, parameterType);

        var count = NativeMethods.CallbackGetParamMapCount(_parameters, index);
        var parameters = _parameters;
        return UmkaMapCopy.CopyStringKeyStringArrayValues(
            count,
            (keys, keyCount, lengths, lengthCount) =>
                NativeMethods.CallbackCopyParamStringKeyMapStringArrayValueEntries(parameters, index, keys, keyCount, lengths, lengthCount),
            (entryIndex, values, valueCount) =>
                NativeMethods.CallbackCopyParamStringKeyMapStringArrayValueData(parameters, index, entryIndex, values, valueCount));
    }

    /// <summary>Tries to copy a map callback argument with string keys and string-array values into a managed dictionary.</summary>
    public bool TryGetStringKeyStringArrayValueMap(
        int index,
        [NotNullWhen(true)] out Dictionary<string, string?[]>? value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetStringKeyStringArrayValueMap(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        value = null;
        return false;
    }

    internal void SetResult(UmkaValue value)
    {
        CheckActive();
        ValidateResult(value);

        switch (value.Kind)
        {
            case UmkaValueKind.Void:
                break;
            case UmkaValueKind.Int:
                NativeMethods.CallbackSetResultInt(_parameters, _result, value.AsInt64());
                break;
            case UmkaValueKind.UInt:
                NativeMethods.CallbackSetResultUInt(_parameters, _result, value.AsUInt64());
                break;
            case UmkaValueKind.Real:
                NativeMethods.CallbackSetResultReal(_parameters, _result, value.AsDouble());
                break;
            case UmkaValueKind.Bool:
                NativeMethods.CallbackSetResultInt(_parameters, _result, value.AsBoolean() ? 1 : 0);
                break;
            case UmkaValueKind.String:
                NativeMethods.CallbackSetResultString(_parameters, _result, value.AsString());
                break;
            case UmkaValueKind.Pointer:
                NativeMethods.CallbackSetResultPointer(_parameters, _result, value.AsPointer());
                break;
            case UmkaValueKind.WeakPointer:
                NativeMethods.CallbackSetResultUInt(_parameters, _result, value.AsWeakPointer());
                break;
            case UmkaValueKind.StaticArray or UmkaValueKind.Struct:
                SetStructuredResult(value);
                break;
            case UmkaValueKind.DynamicArray:
                SetDynamicArrayResult(value);
                break;
            case UmkaValueKind.Map:
                SetMapResult(value);
                break;
            case UmkaValueKind.NativeValue:
                SetNativeValueResult(value.NativeValue);
                break;
            case UmkaValueKind.Any:
                SetAnyResult(value.AnyValue);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Unsupported callback result kind.");
        }
    }

    private void SetStructuredResult(UmkaValue value)
    {
        var size = value.StructuredSize;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            value.CopyStructuredTo(buffer);
            var status = NativeMethods.CallbackSetResultData(_parameters, _result, buffer, size);
            if (status != 0)
                throw new InvalidOperationException($"Callback result could not be copied as fixed-layout {value.Kind}.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void SetDynamicArrayResult(UmkaValue value)
    {
        if (value.IsStringDynamicArray)
        {
            using var strings = new NativeUtf8StringArray(value.GetStringDynamicArray());
            var stringStatus = NativeMethods.CallbackSetResultStringDynamicArray(
                _parameters,
                _result,
                strings.Pointer,
                strings.Length);
            if (stringStatus != 0)
                throw new InvalidOperationException("Callback result could not be copied as a string dynamic array.");
            return;
        }

        if (value.IsNestedStringDynamicArray)
        {
            var rowLengths = value.GetNestedDynamicArrayRowLengths();
            var lengthsBuffer = rowLengths.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked(sizeof(int) * rowLengths.Length));
            try
            {
                if (rowLengths.Length > 0)
                    Marshal.Copy(rowLengths, 0, lengthsBuffer, rowLengths.Length);

                using var strings = new NativeUtf8StringArray(value.GetFlattenedNestedStringDynamicArray());
                var stringStatus = NativeMethods.CallbackSetResultNestedStringArray(
                    _parameters,
                    _result,
                    lengthsBuffer,
                    rowLengths.Length,
                    strings.Pointer,
                    strings.Length);
                if (stringStatus != 0)
                    throw new InvalidOperationException("Callback result could not be copied as a nested string dynamic array.");
                return;
            }
            finally
            {
                if (lengthsBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(lengthsBuffer);
            }
        }

        if (value.IsNestedDynamicArray)
        {
            var rowLengths = value.GetNestedDynamicArrayRowLengths();
            var lengthsBuffer = rowLengths.Length == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(checked(sizeof(int) * rowLengths.Length));
            var valueByteCount = value.StructuredSize;
            var valueBuffer = valueByteCount == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(valueByteCount);
            try
            {
                if (rowLengths.Length > 0)
                    Marshal.Copy(rowLengths, 0, lengthsBuffer, rowLengths.Length);
                if (valueByteCount > 0)
                    value.CopyNestedDynamicArrayElementsTo(valueBuffer);

                var status = NativeMethods.CallbackSetResultNestedDynamicArray(
                    _parameters,
                    _result,
                    lengthsBuffer,
                    rowLengths.Length,
                    valueBuffer,
                    valueByteCount,
                    value.StructuredElementSize);
                if (status != 0)
                    throw new InvalidOperationException("Callback result could not be copied as a nested dynamic array.");
                return;
            }
            finally
            {
                if (valueBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(valueBuffer);
                if (lengthsBuffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(lengthsBuffer);
            }
        }

        var size = value.StructuredSize;
        var buffer = size == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(size);
        try
        {
            if (size > 0)
                value.CopyStructuredTo(buffer);

            var status = NativeMethods.CallbackSetResultDynamicArray(
                _parameters,
                _result,
                buffer,
                value.StructuredLength,
                value.StructuredElementSize);
            if (status != 0)
                throw new InvalidOperationException("Callback result could not be copied as a dynamic array.");
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private void SetMapResult(UmkaValue value)
    {
        var status = UmkaMapNativeWriter.SetCallbackResult(_parameters, _result, value);
        if (status != 0)
            throw new InvalidOperationException("Callback result could not be copied as a map.");
    }

    private void SetNativeValueResult(UmkaNativeValue value)
    {
        var status = NativeMethods.CallbackSetResultNativeValue(_parameters, _result, value.Handle);
        if (status != 0)
            throw new InvalidOperationException("Callback result could not be assigned from the retained native Umka value.");
    }

    private void SetAnyResult(UmkaAnyValue value)
    {
        var status = value.IsNull
            ? NativeMethods.CallbackSetResultAnyNull(_parameters, _result)
            : value.Payload.Kind switch
            {
                UmkaValueKind.Int => NativeMethods.CallbackSetResultAnyInt(_parameters, _result, value.Payload.AsInt64()),
                UmkaValueKind.UInt when value.PayloadType?.Kind == UmkaTypeKind.Character =>
                    NativeMethods.CallbackSetResultAnyChar(_parameters, _result, value.Payload.AsUInt64()),
                UmkaValueKind.UInt => NativeMethods.CallbackSetResultAnyUInt(_parameters, _result, value.Payload.AsUInt64()),
                UmkaValueKind.Real => NativeMethods.CallbackSetResultAnyReal(_parameters, _result, value.Payload.AsDouble()),
                UmkaValueKind.Bool => NativeMethods.CallbackSetResultAnyBool(_parameters, _result, value.Payload.AsBoolean() ? 1 : 0),
                UmkaValueKind.String => NativeMethods.CallbackSetResultAnyString(_parameters, _result, value.Payload.AsString()),
                UmkaValueKind.NativeValue => NativeMethods.CallbackSetResultAnyNativeValue(_parameters, _result, value.Payload.AsNativeValue().Handle),
                _ => 1
            };

        if (status != 0)
            throw new InvalidOperationException("Callback result could not be constructed as an Umka any value.");
    }

    /// <summary>Reads a callback argument as a dynamic value for supported scalar, string, pointer, or weak pointer kinds.</summary>
    public UmkaValue GetValue(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.IsAny)
            return UmkaValue.FromAny(GetAny(index));

        return parameterType.Kind switch
        {
            UmkaTypeKind.SignedInteger => UmkaValue.From(GetInt64(index)),
            UmkaTypeKind.UnsignedInteger => UmkaValue.From(GetUInt64(index)),
            UmkaTypeKind.Real => UmkaValue.From(GetDouble(index)),
            UmkaTypeKind.Boolean => UmkaValue.From(GetBoolean(index)),
            UmkaTypeKind.Character => UmkaValue.From(GetChar(index)),
            UmkaTypeKind.String => UmkaValue.From(GetString(index)),
            UmkaTypeKind.Pointer => UmkaValue.FromPointer(GetPointer(index)),
            UmkaTypeKind.WeakPointer => UmkaValue.FromWeakPointer(GetWeakPointer(index)),
            var kind => throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{GetParameterTypeName(index)}', which cannot be read as a dynamic UmkaValue of kind {kind}.")
        };
    }

    /// <summary>Tries to read a callback argument as a dynamic value for supported scalar, string, pointer, or weak pointer kinds.</summary>
    public bool TryGetValue(int index, out UmkaValue value)
    {
        _ = GetParameterKindOrThrow(index);

        try
        {
            value = GetValue(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (OverflowException)
        {
        }

        value = default;
        return false;
    }

    /// <summary>Retains a callback argument as a runtime-owned native Umka value.</summary>
    public UmkaNativeValue GetNativeValue(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (!parameterType.CanRetainAsNativeValue())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be retained as a native Umka value.");
        }

        var status = NativeMethods.CallbackRetainParam(_parameters, _result, index, out var handle);
        if (status != 0 || handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} with Umka type '{parameterType.TypeName}' could not be retained as a native Umka value.");
        }

        return _runtime.AdoptNativeValue(handle, parameterType);
    }

    /// <summary>Tries to retain a callback argument as a runtime-owned native Umka value.</summary>
    public bool TryGetNativeValue(int index, [NotNullWhen(true)] out UmkaNativeValue? value)
    {
        try
        {
            value = GetNativeValue(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (UmkaException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Reads a callback argument as an Umka <c>any</c> value.</summary>
    public UmkaAnyValue GetAny(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (!parameterType.IsAny)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as an any value.");
        }

        using var retained = GetNativeValue(index);
        return retained.AsAny();
    }

    /// <summary>Tries to read a callback argument as an Umka <c>any</c> value.</summary>
    public bool TryGetAny(int index, [NotNullWhen(true)] out UmkaAnyValue? value)
    {
        try
        {
            value = GetAny(index);
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (UmkaException)
        {
        }

        value = null;
        return false;
    }

    private static T BoxScalar<T>(object? value) => (T)value!;

    private T GetEnumScalar<T>(int index)
    {
        var enumType = typeof(T);
        object rawValue = Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => checked((sbyte)GetInt64(index)),
            TypeCode.Int16 => checked((short)GetInt64(index)),
            TypeCode.Int32 => checked((int)GetInt64(index)),
            TypeCode.Int64 => GetInt64(index),
            TypeCode.Byte => checked((byte)GetUInt64(index)),
            TypeCode.UInt16 => checked((ushort)GetUInt64(index)),
            TypeCode.UInt32 => checked((uint)GetUInt64(index)),
            TypeCode.UInt64 => GetUInt64(index),
            _ => throw new InvalidOperationException($"Enum type {enumType.FullName} has an unsupported underlying storage type.")
        };

        return (T)Enum.ToObject(enumType, rawValue);
    }

    private void EnsureParameterKind(int index, UmkaTypeKind expectedKind)
    {
        var parameterKind = GetParameterKindOrThrow(index);
        if (parameterKind == expectedKind)
            return;

        var typeName = GetParameterTypeName(index);
        throw new InvalidOperationException(
            $"Callback argument {index} has Umka type '{typeName}', which cannot be read as {expectedKind}.");
    }

    private void EnsureParameterKind(int index, UmkaTypeKind expectedKind, UmkaTypeKind alternateKind)
    {
        var parameterKind = GetParameterKindOrThrow(index);
        if (parameterKind == expectedKind || parameterKind == alternateKind)
            return;

        var typeName = GetParameterTypeName(index);
        throw new InvalidOperationException(
            $"Callback argument {index} has Umka type '{typeName}', which cannot be read as {expectedKind} or {alternateKind}.");
    }

    private UmkaTypeKind GetParameterKindOrThrow(int index)
    {
        CheckActive();
        if (index < 0)
            ThrowParameterIndexOutOfRange(index);

        var nativeKind = NativeMethods.CallbackGetParameterKind(_parameters, index);
        if (nativeKind == NativeUmkaTypeKind.None)
            ThrowParameterIndexOutOfRange(index);

        return UmkaTypeInfoFactory.MapKind(nativeKind);
    }

    private UmkaTypeInfo GetParameterTypeInfoOrThrow(int index)
    {
        CheckActive();
        if (index < 0)
            ThrowParameterIndexOutOfRange(index);

        var nativeKind = NativeMethods.CallbackGetParameterKind(_parameters, index);
        if (nativeKind == NativeUmkaTypeKind.None)
            ThrowParameterIndexOutOfRange(index);

        return GetParameterTypeInfo(index, nativeKind);
    }

    private UmkaTypeInfo GetParameterTypeInfo(int index, NativeUmkaTypeKind nativeKind)
    {
        var isEnum = NativeMethods.CallbackGetParameterIsEnum(_parameters, index) != 0;
        return UmkaTypeInfoFactory.Create(
            nativeKind,
            NativeMethods.CallbackGetParameterTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterSize(_parameters, index),
            NativeMethods.CallbackGetParameterItemCount(_parameters, index),
            NativeMethods.CallbackGetParameterHasReferences(_parameters, index) != 0,
            isEnum,
            isEnum ? GetParameterEnumMembers(index) : Array.Empty<UmkaEnumMemberInfo>(),
            NativeMethods.CallbackGetParameterElementKind(_parameters, index),
            NativeMethods.CallbackGetParameterElementTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterElementSize(_parameters, index),
            NativeMethods.CallbackGetParameterElementHasReferences(_parameters, index) != 0,
            NativeMethods.CallbackGetParameterNestedElementKind(_parameters, index),
            NativeMethods.CallbackGetParameterNestedElementTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterNestedElementSize(_parameters, index),
            NativeMethods.CallbackGetParameterNestedElementHasReferences(_parameters, index) != 0,
            NativeMethods.CallbackGetParameterMapKeyKind(_parameters, index),
            NativeMethods.CallbackGetParameterMapKeyTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterMapKeySize(_parameters, index),
            NativeMethods.CallbackGetParameterMapKeyHasReferences(_parameters, index) != 0,
            NativeMethods.CallbackGetParameterMapValueKind(_parameters, index),
            NativeMethods.CallbackGetParameterMapValueTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterMapValueSize(_parameters, index),
            NativeMethods.CallbackGetParameterMapValueHasReferences(_parameters, index) != 0,
            NativeMethods.CallbackGetParameterMapValueElementKind(_parameters, index),
            NativeMethods.CallbackGetParameterMapValueElementTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterMapValueElementSize(_parameters, index),
            NativeMethods.CallbackGetParameterMapValueElementHasReferences(_parameters, index) != 0,
            NativeMethods.CallbackGetParameterIsVariadicParameterList(_parameters, index) != 0);
    }

    private UmkaTypeInfo GetStructuredParameterTypeOrThrow(int index, UmkaTypeKind expectedKind)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind == expectedKind)
            return parameterType;

        throw new InvalidOperationException(
            $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {expectedKind}.");
    }

    private void ThrowParameterIndexOutOfRange(int index)
    {
        throw new ArgumentOutOfRangeException(
            nameof(index),
            index,
            $"Callback frame has {ParameterCount} argument(s).");
    }

    private string GetParameterTypeName(int index)
    {
        var typeName = NativeMethods.CallbackGetParameterTypeName(_parameters, index).ToManagedString();
        return string.IsNullOrWhiteSpace(typeName) ? "unknown" : typeName;
    }

    private static void EnsureReadableStructuredParameter(int index, UmkaTypeInfo parameterType)
    {
        var nativeSize = parameterType.NativeSize;
        if (nativeSize <= 0)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', but its native size is unavailable.");
        }

        if (parameterType.HasReferences)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which contains Umka-managed references and cannot be copied into a managed aggregate value.");
        }
    }

    private static void EnsureReadableDynamicArrayParameter<TElement>(int index, UmkaTypeInfo parameterType)
        where TElement : struct
    {
        if (parameterType.ElementHasReferences)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', whose element type '{parameterType.ElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dynamic array.");
        }

        var elementSize = parameterType.ElementNativeSize;
        if (elementSize <= 0)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', but its native element size is unavailable.");
        }

        var managedElementSize = Marshal.SizeOf<TElement>();
        if (managedElementSize != elementSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}' with native element size {elementSize} bytes, but managed element type {typeof(TElement).FullName} is {managedElementSize} bytes.");
        }
    }

    private static void EnsureReadableStringDynamicArrayParameter(int index, UmkaTypeInfo parameterType)
    {
        if (!parameterType.CanReadAsStringArray())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be copied into a managed string array.");
        }
    }

    private static void EnsureReadableNestedDynamicArrayParameter<TElement>(int index, UmkaTypeInfo parameterType)
        where TElement : struct
    {
        if (parameterType.ElementKind != UmkaTypeKind.DynamicArray)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be copied into a managed jagged array because its element type is '{parameterType.ElementTypeName ?? "unknown"}'.");
        }

        if (parameterType.NestedElementHasReferences)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', whose inner element type '{parameterType.NestedElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed jagged array.");
        }

        var elementSize = parameterType.NestedElementNativeSize;
        if (elementSize <= 0)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', but its native inner element size is unavailable.");
        }

        var managedElementSize = Marshal.SizeOf<TElement>();
        if (managedElementSize != elementSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}' with native inner element size {elementSize} bytes, but managed inner element type {typeof(TElement).FullName} is {managedElementSize} bytes.");
        }
    }

    private static void EnsureReadableNestedStringArrayParameter(int index, UmkaTypeInfo parameterType)
    {
        if (!parameterType.CanReadAsNestedStringArray())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be copied into a managed jagged string array.");
        }
    }

    private static void EnsureReadableMapParameter<TKey, TValue>(int index, UmkaTypeInfo parameterType)
        where TKey : struct
        where TValue : struct
    {
        if (parameterType.MapKeyHasReferences)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', whose key type '{parameterType.MapKeyTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dictionary.");
        }

        if (parameterType.MapValueHasReferences)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', whose value type '{parameterType.MapValueTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied into a managed dictionary.");
        }

        if (parameterType.MapKeyNativeSize <= 0 || parameterType.MapValueNativeSize <= 0)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', but its native key/value size metadata is unavailable.");
        }

        var managedKeySize = Marshal.SizeOf<TKey>();
        if (managedKeySize != parameterType.MapKeyNativeSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}' with native key size {parameterType.MapKeyNativeSize} bytes, but managed key type {typeof(TKey).FullName} is {managedKeySize} bytes.");
        }

        var managedValueSize = Marshal.SizeOf<TValue>();
        if (managedValueSize != parameterType.MapValueNativeSize)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}' with native value size {parameterType.MapValueNativeSize} bytes, but managed value type {typeof(TValue).FullName} is {managedValueSize} bytes.");
        }
    }

    private static void EnsureReadableStringKeyMapParameter<TValue>(int index, UmkaTypeInfo parameterType)
        where TValue : struct
    {
        if (!parameterType.CanReadAsStringKeyMap<TValue>())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with string keys and {typeof(TValue).FullName} values.");
        }
    }

    private static void EnsureReadableStringValueMapParameter<TKey>(int index, UmkaTypeInfo parameterType)
        where TKey : struct
    {
        if (!parameterType.CanReadAsStringValueMap<TKey>())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and string values.");
        }
    }

    private static void EnsureReadableStringMapParameter(int index, UmkaTypeInfo parameterType)
    {
        if (!parameterType.CanReadAsStringMap())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with string keys and string values.");
        }
    }

    private static void EnsureReadableDynamicArrayValueMapParameter<TKey, TElement>(int index, UmkaTypeInfo parameterType)
        where TKey : struct
        where TElement : struct
    {
        if (!parameterType.CanReadAsDynamicArrayValueMap<TKey, TElement>())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and {typeof(TElement).FullName}[] values.");
        }
    }

    private static void EnsureReadableStringKeyDynamicArrayValueMapParameter<TElement>(int index, UmkaTypeInfo parameterType)
        where TElement : struct
    {
        if (!parameterType.CanReadAsStringKeyDynamicArrayValueMap<TElement>())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with string keys and {typeof(TElement).FullName}[] values.");
        }
    }

    private static void EnsureReadableStringArrayValueMapParameter<TKey>(int index, UmkaTypeInfo parameterType)
        where TKey : struct
    {
        if (!parameterType.CanReadAsStringArrayValueMap<TKey>())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with {typeof(TKey).FullName} keys and string-array values.");
        }
    }

    private static void EnsureReadableStringKeyStringArrayValueMapParameter(int index, UmkaTypeInfo parameterType)
    {
        if (!parameterType.CanReadAsStringKeyStringArrayValueMap())
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka map type '{parameterType.TypeName}', which cannot be copied into a managed dictionary with string keys and string-array values.");
        }
    }

    private UmkaTypeInfo GetMapParameterTypeInfoOrThrow(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        if (parameterType.Kind != UmkaTypeKind.Map)
        {
            throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{parameterType.TypeName}', which cannot be read as {UmkaTypeKind.Map}.");
        }

        return parameterType;
    }

    private static void ValidateNoManagedReferences<T>() where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException(
                $"Managed type {typeof(T).FullName} contains managed references and cannot receive Umka aggregate storage.");
        }
    }

    private void ValidateResult(UmkaValue value)
    {
        var nativeKind = NativeMethods.CallbackGetResultKind(_parameters, _result);
        if (value.Kind == UmkaValueKind.Any)
        {
            ValidateAnyResult(GetResultTypeInfo(nativeKind), value.AnyValue, nameof(value));
            return;
        }

        if (value.Kind == UmkaValueKind.NativeValue)
        {
            ValidateNativeValueResult(GetResultTypeInfo(nativeKind), value.NativeValue, nameof(value));
            return;
        }

        if (!IsSupportedResultKind(nativeKind))
        {
            throw new ArgumentException(
                UnsupportedResultMessage(nativeKind),
                nameof(value));
        }

        if (!IsCompatibleResultKind(nativeKind, value.Kind))
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{GetResultTypeName()}', but value kind {value.Kind} was provided.",
                nameof(value));
        }

        if (value.Kind is UmkaValueKind.StaticArray or UmkaValueKind.Struct)
        {
            ValidateStructuredResult(nativeKind, GetResultTypeInfo(nativeKind), value);
            return;
        }

        if (value.Kind == UmkaValueKind.DynamicArray)
        {
            ValidateDynamicArrayResult(GetResultTypeInfo(nativeKind), value);
            return;
        }

        if (value.Kind == UmkaValueKind.Map)
        {
            ValidateMapResult(GetResultTypeInfo(nativeKind), value);
            return;
        }

        ValidateResultRange(nativeKind, value);
    }

    private static void ValidateStructuredResult(NativeUmkaTypeKind nativeKind, UmkaTypeInfo resultType, UmkaValue value)
    {
        if (resultType.HasReferences)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', which contains Umka-managed references and cannot be copied from a managed aggregate value.",
                nameof(value));
        }

        var nativeSize = resultType.NativeSize;
        if (nativeSize <= 0)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but its native size is unavailable.",
                nameof(value));
        }

        if (nativeKind == NativeUmkaTypeKind.StaticArray)
        {
            var nativeLength = resultType.ItemCount;
            if (value.StructuredLength != nativeLength)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}' with {nativeLength} item(s), but value kind {value.Kind} has {value.StructuredLength} item(s).",
                    nameof(value));
            }
        }

        if (value.StructuredSize != nativeSize)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}' with native size {nativeSize} bytes, but value kind {value.Kind} has size {value.StructuredSize} bytes.",
                nameof(value));
        }
    }

    private static void ValidateDynamicArrayResult(UmkaTypeInfo resultType, UmkaValue value)
    {
        if (resultType.CanReadAsStringArray())
        {
            if (!value.IsStringDynamicArray)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}', but the dynamic array value is not a string array.",
                    nameof(value));
            }

            return;
        }

        if (value.IsStringDynamicArray)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but a string dynamic array value was provided.",
                nameof(value));
        }

        if (resultType.CanReadAsNestedStringArray())
        {
            if (!value.IsNestedStringDynamicArray)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}', but the dynamic array value is not a nested string array.",
                    nameof(value));
            }

            return;
        }

        if (value.IsNestedStringDynamicArray)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but a nested string dynamic array value was provided.",
                nameof(value));
        }

        if (resultType.Kind == UmkaTypeKind.DynamicArray && resultType.ElementKind == UmkaTypeKind.DynamicArray)
        {
            if (!value.IsNestedDynamicArray)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}', but the dynamic array value is not a nested dynamic array.",
                    nameof(value));
            }

            if (resultType.NestedElementHasReferences)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}', whose inner element type '{resultType.NestedElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied from a managed nested dynamic array value.",
                    nameof(value));
            }

            var nestedElementSize = resultType.NestedElementNativeSize;
            if (nestedElementSize <= 0)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}', but its native inner element size is unavailable.",
                    nameof(value));
            }

            if (value.StructuredElementSize != nestedElementSize)
            {
                throw new ArgumentException(
                    $"Callback result expects Umka type '{resultType.TypeName}' with native inner element size {nestedElementSize} bytes, but nested dynamic array value elements have size {value.StructuredElementSize} bytes.",
                    nameof(value));
            }

            return;
        }

        if (value.IsNestedDynamicArray)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but a nested dynamic array value was provided.",
                nameof(value));
        }

        if (resultType.ElementHasReferences)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', whose element type '{resultType.ElementTypeName ?? "unknown"}' contains Umka-managed references and cannot be copied from a managed dynamic array value.",
                nameof(value));
        }

        var elementSize = resultType.ElementNativeSize;
        if (elementSize <= 0)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but its native element size is unavailable.",
                nameof(value));
        }

        if (value.StructuredElementSize != elementSize)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}' with native element size {elementSize} bytes, but dynamic array value elements have size {value.StructuredElementSize} bytes.",
                nameof(value));
        }
    }

    private static void ValidateMapResult(UmkaTypeInfo resultType, UmkaValue value)
    {
        UmkaMapValueCompatibility.Validate(
            "Callback result",
            resultType,
            value,
            nameof(value));
    }

    private static bool CanReturnStructuredValue(NativeUmkaTypeKind nativeKind, UmkaTypeInfo resultType, UmkaValue value)
    {
        if (resultType.HasReferences || resultType.NativeSize <= 0)
            return false;

        if (nativeKind == NativeUmkaTypeKind.StaticArray && value.StructuredLength != resultType.ItemCount)
            return false;

        return value.StructuredSize == resultType.NativeSize;
    }

    private static bool CanReturnDynamicArrayValue(UmkaTypeInfo resultType, UmkaValue value) =>
        resultType.Kind == UmkaTypeKind.DynamicArray
        && (resultType.CanReadAsStringArray()
            ? value.IsStringDynamicArray
            : resultType.CanReadAsNestedStringArray()
                ? value.IsNestedStringDynamicArray
                : resultType.ElementKind == UmkaTypeKind.DynamicArray
                    ? value.IsNestedDynamicArray
                        && !value.IsNestedStringDynamicArray
                        && !resultType.NestedElementHasReferences
                        && resultType.NestedElementNativeSize > 0
                        && value.StructuredElementSize == resultType.NestedElementNativeSize
                    : !resultType.ElementHasReferences
                        && !value.IsStringDynamicArray
                        && !value.IsNestedDynamicArray
                        && resultType.ElementNativeSize > 0
                        && value.StructuredElementSize == resultType.ElementNativeSize);

    private static bool CanReturnMapValue(UmkaTypeInfo resultType, UmkaValue value) =>
        UmkaMapValueCompatibility.CanWrite(resultType, value);

    private bool CanReturnNativeValue(UmkaTypeInfo resultType, UmkaNativeValue value) =>
        resultType.CanRetainAsNativeValue()
        && value.CanAssignTo(_runtime, resultType);

    private bool CanReturnAnyValue(UmkaTypeInfo resultType, UmkaAnyValue value) =>
        resultType.IsAny
        && CanWriteAnyPayload(value);

    private bool CanWriteAnyPayload(UmkaAnyValue value)
    {
        if (value.IsNull)
            return true;

        return value.Payload.Kind switch
        {
            UmkaValueKind.Int
                or UmkaValueKind.UInt
                or UmkaValueKind.Real
                or UmkaValueKind.Bool
                or UmkaValueKind.String => true,
            UmkaValueKind.NativeValue => value.Payload.TryAsNativeValue(out var nativeValue)
                && !nativeValue.IsDisposed
                && ReferenceEquals(nativeValue.Runtime, _runtime)
                && nativeValue.Type.CanRetainAsNativeValue()
                && nativeValue.Type.Kind != UmkaTypeKind.Fiber,
            _ => false
        };
    }

    private void ValidateNativeValueResult(UmkaTypeInfo resultType, UmkaNativeValue nativeValue, string parameterName)
    {
        if (!resultType.CanRetainAsNativeValue())
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', which cannot be assigned from a retained native Umka value.",
                parameterName);
        }

        ObjectDisposedException.ThrowIf(nativeValue.IsDisposed, nativeValue);

        if (!ReferenceEquals(nativeValue.Runtime, _runtime))
        {
            throw new InvalidOperationException(
                "Callback result expects a native Umka value owned by the same runtime.");
        }

        if (!nativeValue.CanAssignTo(_runtime, resultType))
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', but retained native value type '{nativeValue.Type.TypeName}' was provided.",
                parameterName);
        }
    }

    private void ValidateAnyResult(UmkaTypeInfo resultType, UmkaAnyValue anyValue, string parameterName)
    {
        if (!resultType.IsAny)
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{resultType.TypeName}', which is not Umka any.",
                parameterName);
        }

        ValidateAnyPayload("Callback result", anyValue, parameterName);
    }

    private void ValidateAnyPayload(string owner, UmkaAnyValue anyValue, string parameterName)
    {
        if (anyValue.IsNull)
            return;

        switch (anyValue.Payload.Kind)
        {
            case UmkaValueKind.Int:
            case UmkaValueKind.UInt:
            case UmkaValueKind.Real:
            case UmkaValueKind.Bool:
            case UmkaValueKind.String:
                return;

            case UmkaValueKind.NativeValue:
            {
                var nativeValue = anyValue.Payload.AsNativeValue();
                ObjectDisposedException.ThrowIf(nativeValue.IsDisposed, nativeValue);
                if (!ReferenceEquals(nativeValue.Runtime, _runtime))
                    throw new InvalidOperationException($"{owner} expects an any payload owned by the same runtime.");
                if (nativeValue.Type.Kind == UmkaTypeKind.Fiber)
                    throw new ArgumentException($"{owner} cannot use a fiber value as an any payload because host-side fiber resume is not supported.", parameterName);
                if (!nativeValue.Type.CanRetainAsNativeValue())
                    throw new ArgumentException($"{owner} cannot use Umka type '{nativeValue.Type.TypeName}' as a retained any payload.", parameterName);
                return;
            }

            case UmkaValueKind.StaticArray:
            case UmkaValueKind.Struct:
            case UmkaValueKind.DynamicArray:
            case UmkaValueKind.Map:
                throw new ArgumentException(
                    $"{owner} cannot construct an any payload from managed {anyValue.Payload.Kind} bytes because the value does not carry concrete Umka type metadata. Retain an Umka value and pass it with UmkaAnyValue.From(UmkaNativeValue).",
                    parameterName);

            default:
                throw new ArgumentException($"{owner} does not support value kind {anyValue.Payload.Kind} as an any payload.", parameterName);
        }
    }

    private UmkaTypeInfo GetResultTypeInfo(NativeUmkaTypeKind nativeKind)
    {
        var isEnum = NativeMethods.CallbackGetResultIsEnum(_parameters, _result) != 0;
        return UmkaTypeInfoFactory.Create(
            nativeKind,
            NativeMethods.CallbackGetResultTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultSize(_parameters, _result),
            NativeMethods.CallbackGetResultItemCount(_parameters, _result),
            NativeMethods.CallbackGetResultHasReferences(_parameters, _result) != 0,
            isEnum,
            isEnum ? GetResultEnumMembers() : Array.Empty<UmkaEnumMemberInfo>(),
            NativeMethods.CallbackGetResultElementKind(_parameters, _result),
            NativeMethods.CallbackGetResultElementTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultElementSize(_parameters, _result),
            NativeMethods.CallbackGetResultElementHasReferences(_parameters, _result) != 0,
            NativeMethods.CallbackGetResultNestedElementKind(_parameters, _result),
            NativeMethods.CallbackGetResultNestedElementTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultNestedElementSize(_parameters, _result),
            NativeMethods.CallbackGetResultNestedElementHasReferences(_parameters, _result) != 0,
            NativeMethods.CallbackGetResultMapKeyKind(_parameters, _result),
            NativeMethods.CallbackGetResultMapKeyTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultMapKeySize(_parameters, _result),
            NativeMethods.CallbackGetResultMapKeyHasReferences(_parameters, _result) != 0,
            NativeMethods.CallbackGetResultMapValueKind(_parameters, _result),
            NativeMethods.CallbackGetResultMapValueTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultMapValueSize(_parameters, _result),
            NativeMethods.CallbackGetResultMapValueHasReferences(_parameters, _result) != 0,
            NativeMethods.CallbackGetResultMapValueElementKind(_parameters, _result),
            NativeMethods.CallbackGetResultMapValueElementTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultMapValueElementSize(_parameters, _result),
            NativeMethods.CallbackGetResultMapValueElementHasReferences(_parameters, _result) != 0,
            NativeMethods.CallbackGetResultIsVariadicParameterList(_parameters, _result) != 0);
    }

    private UmkaEnumMemberInfo[] GetParameterEnumMembers(int index)
    {
        var count = NativeMethods.CallbackGetParameterEnumMemberCount(_parameters, index);
        if (count <= 0)
            return Array.Empty<UmkaEnumMemberInfo>();

        var members = new UmkaEnumMemberInfo[count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new UmkaEnumMemberInfo(
                NativeMethods.CallbackGetParameterEnumMemberName(_parameters, index, i).ToManagedString() ?? "unknown",
                NativeMethods.CallbackGetParameterEnumMemberSignedValue(_parameters, index, i),
                NativeMethods.CallbackGetParameterEnumMemberUnsignedValue(_parameters, index, i));
        }

        return members;
    }

    private UmkaEnumMemberInfo[] GetResultEnumMembers()
    {
        var count = NativeMethods.CallbackGetResultEnumMemberCount(_parameters, _result);
        if (count <= 0)
            return Array.Empty<UmkaEnumMemberInfo>();

        var members = new UmkaEnumMemberInfo[count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new UmkaEnumMemberInfo(
                NativeMethods.CallbackGetResultEnumMemberName(_parameters, _result, i).ToManagedString() ?? "unknown",
                NativeMethods.CallbackGetResultEnumMemberSignedValue(_parameters, _result, i),
                NativeMethods.CallbackGetResultEnumMemberUnsignedValue(_parameters, _result, i));
        }

        return members;
    }

    private static bool IsSupportedResultKind(NativeUmkaTypeKind kind) =>
        kind is NativeUmkaTypeKind.Void
            or NativeUmkaTypeKind.Int8
            or NativeUmkaTypeKind.Int16
            or NativeUmkaTypeKind.Int32
            or NativeUmkaTypeKind.Int
            or NativeUmkaTypeKind.UInt8
            or NativeUmkaTypeKind.UInt16
            or NativeUmkaTypeKind.UInt32
            or NativeUmkaTypeKind.UInt
            or NativeUmkaTypeKind.Bool
            or NativeUmkaTypeKind.Char
            or NativeUmkaTypeKind.Real32
            or NativeUmkaTypeKind.Real
            or NativeUmkaTypeKind.Pointer
            or NativeUmkaTypeKind.WeakPointer
            or NativeUmkaTypeKind.String
            or NativeUmkaTypeKind.StaticArray
            or NativeUmkaTypeKind.Struct
            or NativeUmkaTypeKind.DynamicArray
            or NativeUmkaTypeKind.Map;

    private string UnsupportedResultMessage(NativeUmkaTypeKind nativeKind)
    {
        var typeName = GetResultTypeName();
        if (nativeKind == NativeUmkaTypeKind.Map)
        {
            return $"Callback result expects Umka map type '{typeName}', but that map key/value shape cannot be constructed from a managed map value.";
        }

        return $"Callback result expects Umka type '{typeName}', which UmkaSharp does not support as a callback result.";
    }

    private static bool IsCompatibleResultKind(NativeUmkaTypeKind nativeKind, UmkaValueKind valueKind) =>
        nativeKind switch
        {
            NativeUmkaTypeKind.Void => valueKind == UmkaValueKind.Void,
            NativeUmkaTypeKind.Int8
                or NativeUmkaTypeKind.Int16
                or NativeUmkaTypeKind.Int32
                or NativeUmkaTypeKind.Int => valueKind == UmkaValueKind.Int,
            NativeUmkaTypeKind.UInt8
                or NativeUmkaTypeKind.UInt16
                or NativeUmkaTypeKind.UInt32
                or NativeUmkaTypeKind.UInt => valueKind == UmkaValueKind.UInt,
            NativeUmkaTypeKind.Bool => valueKind == UmkaValueKind.Bool,
            NativeUmkaTypeKind.Char => valueKind is UmkaValueKind.Int or UmkaValueKind.UInt,
            NativeUmkaTypeKind.Real32 or NativeUmkaTypeKind.Real => valueKind == UmkaValueKind.Real,
            NativeUmkaTypeKind.Pointer => valueKind == UmkaValueKind.Pointer,
            NativeUmkaTypeKind.WeakPointer => valueKind == UmkaValueKind.WeakPointer,
            NativeUmkaTypeKind.String => valueKind == UmkaValueKind.String,
            NativeUmkaTypeKind.StaticArray => valueKind == UmkaValueKind.StaticArray,
            NativeUmkaTypeKind.Struct => valueKind == UmkaValueKind.Struct,
            NativeUmkaTypeKind.DynamicArray => valueKind == UmkaValueKind.DynamicArray,
            NativeUmkaTypeKind.Map => valueKind == UmkaValueKind.Map,
            _ => false
        };

    private void ValidateResultRange(NativeUmkaTypeKind nativeKind, UmkaValue value)
    {
        switch (nativeKind)
        {
            case NativeUmkaTypeKind.Int8:
                ValidateSignedRange(value.AsInt64(), sbyte.MinValue, sbyte.MaxValue);
                break;
            case NativeUmkaTypeKind.Int16:
                ValidateSignedRange(value.AsInt64(), short.MinValue, short.MaxValue);
                break;
            case NativeUmkaTypeKind.Int32:
                ValidateSignedRange(value.AsInt64(), int.MinValue, int.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt8:
                ValidateUnsignedRange(value.AsUInt64(), byte.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt16:
                ValidateUnsignedRange(value.AsUInt64(), ushort.MaxValue);
                break;
            case NativeUmkaTypeKind.UInt32:
                ValidateUnsignedRange(value.AsUInt64(), uint.MaxValue);
                break;
            case NativeUmkaTypeKind.Char when value.Kind == UmkaValueKind.Int:
                ValidateSignedRange(value.AsInt64(), byte.MinValue, byte.MaxValue);
                break;
            case NativeUmkaTypeKind.Char:
                ValidateUnsignedRange(value.AsUInt64(), byte.MaxValue);
                break;
            case NativeUmkaTypeKind.Real32:
                ValidateSingleRange(value.AsDouble());
                break;
        }
    }

    private static bool IsResultInRange(NativeUmkaTypeKind nativeKind, UmkaValue value) =>
        nativeKind switch
        {
            NativeUmkaTypeKind.Int8 => IsSignedInRange(value.AsInt64(), sbyte.MinValue, sbyte.MaxValue),
            NativeUmkaTypeKind.Int16 => IsSignedInRange(value.AsInt64(), short.MinValue, short.MaxValue),
            NativeUmkaTypeKind.Int32 => IsSignedInRange(value.AsInt64(), int.MinValue, int.MaxValue),
            NativeUmkaTypeKind.UInt8 => value.AsUInt64() <= byte.MaxValue,
            NativeUmkaTypeKind.UInt16 => value.AsUInt64() <= ushort.MaxValue,
            NativeUmkaTypeKind.UInt32 => value.AsUInt64() <= uint.MaxValue,
            NativeUmkaTypeKind.Char when value.Kind == UmkaValueKind.Int => IsSignedInRange(value.AsInt64(), byte.MinValue, byte.MaxValue),
            NativeUmkaTypeKind.Char => value.AsUInt64() <= byte.MaxValue,
            NativeUmkaTypeKind.Real32 => !UmkaSingleConversion.IsOutsideFiniteSingleRange(value.AsDouble()),
            _ => true
        };

    private static bool IsSignedInRange(long value, long minValue, long maxValue) =>
        value >= minValue && value <= maxValue;

    private void ValidateSignedRange(long value, long minValue, long maxValue)
    {
        if (value < minValue || value > maxValue)
            ThrowRangeError(value.ToString(System.Globalization.CultureInfo.InvariantCulture), minValue, maxValue);
    }

    private void ValidateUnsignedRange(ulong value, ulong maxValue)
    {
        if (value > maxValue)
            ThrowRangeError(value.ToString(System.Globalization.CultureInfo.InvariantCulture), 0UL, maxValue);
    }

    private void ValidateSingleRange(double value)
    {
        if (UmkaSingleConversion.IsOutsideFiniteSingleRange(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                UmkaSingleConversion.Format(value),
                $"Callback result expects Umka type '{GetResultTypeName()}' in finite range {UmkaSingleConversion.FiniteRangeDescription}.");
        }
    }

    private void ThrowRangeError<T>(string value, T minValue, T maxValue)
    {
        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            $"Callback result expects Umka type '{GetResultTypeName()}' in range {minValue}..{maxValue}.");
    }

    private string GetResultTypeName()
    {
        var typeName = NativeMethods.CallbackGetResultTypeName(_parameters, _result).ToManagedString();
        return string.IsNullOrWhiteSpace(typeName) ? "unknown" : typeName;
    }

    private UmkaTypeInfo CheckActiveAndGetResultType()
    {
        CheckActive();
        return GetResultTypeInfo(NativeMethods.CallbackGetResultKind(_parameters, _result));
    }

    private void CheckActive()
    {
        if (_runtime is null)
            throw new InvalidOperationException("Umka callback frame is not initialized.");

        _runtime.CheckCallbackFrameActive(_frameId);
    }
}
