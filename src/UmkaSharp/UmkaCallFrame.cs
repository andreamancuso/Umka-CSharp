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

    /// <summary>Returns whether the argument metadata can be read as a dynamic value for supported scalar, string, or pointer kinds without reading the argument.</summary>
    public bool CanReadArgumentAsValue(int index)
    {
        var parameterType = GetParameterTypeInfoOrThrow(index);
        return parameterType.Kind != UmkaTypeKind.Void && parameterType.CanReadAsValue();
    }

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

    /// <summary>Returns whether the supplied value can be returned from this callback frame without writing it.</summary>
    public bool CanReturn(UmkaValue value)
    {
        CheckActive();

        var nativeKind = NativeMethods.CallbackGetResultKind(_parameters, _result);
        if (!IsSupportedResultKind(nativeKind) || !IsCompatibleResultKind(nativeKind, value.Kind))
            return false;

        return value.Kind is UmkaValueKind.StaticArray or UmkaValueKind.Struct
            ? CanReturnStructuredValue(nativeKind, GetResultTypeInfo(nativeKind), value)
            : IsResultInRange(nativeKind, value);
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
            case UmkaValueKind.StaticArray or UmkaValueKind.Struct:
                SetStructuredResult(value);
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

    /// <summary>Reads a callback argument as a dynamic value for supported scalar, string, or pointer kinds.</summary>
    public UmkaValue GetValue(int index)
    {
        return GetParameterKindOrThrow(index) switch
        {
            UmkaTypeKind.SignedInteger => UmkaValue.From(GetInt64(index)),
            UmkaTypeKind.UnsignedInteger => UmkaValue.From(GetUInt64(index)),
            UmkaTypeKind.Real => UmkaValue.From(GetDouble(index)),
            UmkaTypeKind.Boolean => UmkaValue.From(GetBoolean(index)),
            UmkaTypeKind.Character => UmkaValue.From(GetChar(index)),
            UmkaTypeKind.String => UmkaValue.From(GetString(index)),
            UmkaTypeKind.Pointer => UmkaValue.FromPointer(GetPointer(index)),
            var kind => throw new InvalidOperationException(
                $"Callback argument {index} has Umka type '{GetParameterTypeName(index)}', which cannot be read as a dynamic UmkaValue of kind {kind}.")
        };
    }

    /// <summary>Tries to read a callback argument as a dynamic value for supported scalar, string, or pointer kinds.</summary>
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

    private UmkaTypeInfo GetParameterTypeInfo(int index, NativeUmkaTypeKind nativeKind) =>
        UmkaTypeInfoFactory.Create(
            nativeKind,
            NativeMethods.CallbackGetParameterTypeName(_parameters, index).ToManagedString(),
            NativeMethods.CallbackGetParameterSize(_parameters, index),
            NativeMethods.CallbackGetParameterItemCount(_parameters, index),
            NativeMethods.CallbackGetParameterHasReferences(_parameters, index) != 0);

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

        if (!IsSupportedResultKind(nativeKind))
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{GetResultTypeName()}', which UmkaSharp does not support as a callback result.",
                nameof(value));
        }

        if (!IsCompatibleResultKind(nativeKind, value.Kind))
        {
            throw new ArgumentException(
                $"Callback result expects Umka type '{GetResultTypeName()}', but value kind {value.Kind} was provided.",
                nameof(value));
        }

        if (value.Kind is UmkaValueKind.StaticArray or UmkaValueKind.Struct)
            ValidateStructuredResult(nativeKind, GetResultTypeInfo(nativeKind), value);

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

    private static bool CanReturnStructuredValue(NativeUmkaTypeKind nativeKind, UmkaTypeInfo resultType, UmkaValue value)
    {
        if (resultType.HasReferences || resultType.NativeSize <= 0)
            return false;

        if (nativeKind == NativeUmkaTypeKind.StaticArray && value.StructuredLength != resultType.ItemCount)
            return false;

        return value.StructuredSize == resultType.NativeSize;
    }

    private UmkaTypeInfo GetResultTypeInfo(NativeUmkaTypeKind nativeKind) =>
        UmkaTypeInfoFactory.Create(
            nativeKind,
            NativeMethods.CallbackGetResultTypeName(_parameters, _result).ToManagedString(),
            NativeMethods.CallbackGetResultSize(_parameters, _result),
            NativeMethods.CallbackGetResultItemCount(_parameters, _result),
            NativeMethods.CallbackGetResultHasReferences(_parameters, _result) != 0);

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
            or NativeUmkaTypeKind.String
            or NativeUmkaTypeKind.StaticArray
            or NativeUmkaTypeKind.Struct;

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
            NativeUmkaTypeKind.String => valueKind == UmkaValueKind.String,
            NativeUmkaTypeKind.StaticArray => valueKind == UmkaValueKind.StaticArray,
            NativeUmkaTypeKind.Struct => valueKind == UmkaValueKind.Struct,
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
