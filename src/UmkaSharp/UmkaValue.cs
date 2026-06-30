namespace UmkaSharp;

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Supported managed value kinds for the first UmkaSharp marshalling layer.</summary>
#pragma warning disable CA1720 // Names intentionally match Umka-facing value categories.
public enum UmkaValueKind
{
    /// <summary>No value.</summary>
    Void,
    /// <summary>Signed integer value.</summary>
    Int,
    /// <summary>Unsigned integer value.</summary>
    UInt,
    /// <summary>Double-precision real value.</summary>
    Real,
    /// <summary>Boolean value.</summary>
    Bool,
    /// <summary>UTF-8 string value.</summary>
    String,
    /// <summary>Opaque pointer value.</summary>
    Pointer,
    /// <summary>Fixed-size static array value.</summary>
    StaticArray,
    /// <summary>Fixed-size struct value.</summary>
    Struct,
    /// <summary>Dynamic array value copied into or out of Umka-owned storage.</summary>
    DynamicArray,
    /// <summary>Opaque Umka weak pointer handle value.</summary>
    WeakPointer
}
#pragma warning restore CA1720

/// <summary>A typed value container used by callbacks and dynamic calls.</summary>
public readonly struct UmkaValue
{
    private readonly long _int64Value;
    private readonly ulong _uint64Value;
    private readonly double _doubleValue;
    private readonly string? _stringValue;
    private readonly IntPtr _pointerValue;
    private readonly StructuredValue? _structuredValue;

    private UmkaValue(
        UmkaValueKind kind,
        long int64Value = 0,
        ulong uint64Value = 0,
        double doubleValue = 0,
        string? stringValue = null,
        IntPtr pointerValue = default,
        StructuredValue? structuredValue = null)
    {
        Kind = kind;
        _int64Value = int64Value;
        _uint64Value = uint64Value;
        _doubleValue = doubleValue;
        _stringValue = stringValue;
        _pointerValue = pointerValue;
        _structuredValue = structuredValue;
    }

    /// <summary>Gets the kind of value stored in this container.</summary>
    public UmkaValueKind Kind { get; }

    /// <summary>Gets a boxed diagnostic view of the stored value.</summary>
    public object? Value =>
        Kind switch
        {
            UmkaValueKind.Int => _int64Value,
            UmkaValueKind.UInt => _uint64Value,
            UmkaValueKind.Real => _doubleValue,
            UmkaValueKind.Bool => _int64Value != 0,
            UmkaValueKind.String => _stringValue,
            UmkaValueKind.Pointer => _pointerValue,
            UmkaValueKind.WeakPointer => _uint64Value,
            UmkaValueKind.StaticArray or UmkaValueKind.Struct or UmkaValueKind.DynamicArray => _structuredValue?.Value,
            _ => null
        };

    /// <summary>Gets a void value.</summary>
    public static UmkaValue Void => default;

    /// <summary>Creates a signed integer value.</summary>
    public static UmkaValue From(sbyte value) => From((long)value);

    /// <summary>Creates a signed integer value.</summary>
    public static UmkaValue From(short value) => From((long)value);

    /// <summary>Creates a signed integer value.</summary>
    public static UmkaValue From(int value) => From((long)value);

    /// <summary>Creates a signed integer value.</summary>
    public static UmkaValue From(long value) => new(UmkaValueKind.Int, int64Value: value);

    /// <summary>Creates an unsigned integer value.</summary>
    public static UmkaValue From(byte value) => From((ulong)value);

    /// <summary>Creates an unsigned integer value.</summary>
    public static UmkaValue From(ushort value) => From((ulong)value);

    /// <summary>Creates an unsigned integer value.</summary>
    public static UmkaValue From(uint value) => From((ulong)value);

    /// <summary>Creates an unsigned integer value.</summary>
    public static UmkaValue From(ulong value) => new(UmkaValueKind.UInt, uint64Value: value);

    /// <summary>Creates an unsigned integer value for an Umka character parameter.</summary>
    public static UmkaValue From(char value)
    {
        if (value > byte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                (int)value,
                $"Umka char values must be in range {byte.MinValue}..{byte.MaxValue}.");
        }

        return From((ulong)value);
    }

    /// <summary>Creates a real value.</summary>
    public static UmkaValue From(float value) => From((double)value);

    /// <summary>Creates a real value.</summary>
    public static UmkaValue From(double value) => new(UmkaValueKind.Real, doubleValue: value);

    /// <summary>Creates a Boolean value.</summary>
    public static UmkaValue From(bool value) => new(UmkaValueKind.Bool, int64Value: value ? 1 : 0);

    /// <summary>Creates a string value.</summary>
    public static UmkaValue From(string? value)
    {
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, nameof(value));
        return new(UmkaValueKind.String, stringValue: value);
    }

    /// <summary>Creates an integer value from an enum's underlying signed or unsigned storage.</summary>
    public static UmkaValue FromEnum<TEnum>(TEnum value) where TEnum : struct, Enum =>
        UmkaEnumConversion.ToUmkaValue(value);

    /// <summary>Creates a pointer value.</summary>
    public static UmkaValue FromPointer(IntPtr value) => new(UmkaValueKind.Pointer, pointerValue: value);

    /// <summary>Creates an opaque Umka weak pointer handle value.</summary>
    public static UmkaValue FromWeakPointer(ulong value) => new(UmkaValueKind.WeakPointer, uint64Value: value);

    /// <summary>Creates a scalar value from a supported managed scalar, string, pointer, enum, host handle, or existing Umka value.</summary>
    public static UmkaValue FromScalar<T>(T value)
    {
        if (typeof(T) == typeof(string))
            return From((string?)(object?)value);
        if (value is null)
            throw new NotSupportedException($"FromScalar<T>() does not support null values of type {typeof(T).FullName}.");

        return value switch
        {
            UmkaValue existing => existing,
            sbyte signed => From(signed),
            short signed => From(signed),
            int signed => From(signed),
            long signed => From(signed),
            byte unsigned => From(unsigned),
            ushort unsigned => From(unsigned),
            uint unsigned => From(unsigned),
            ulong unsigned => From(unsigned),
            char character => From(character),
            float real => From(real),
            double real => From(real),
            bool boolean => From(boolean),
            string text => From(text),
            IntPtr pointer => FromPointer(pointer),
            UmkaHostHandle handle => FromHostHandle(handle),
            Enum enumValue => UmkaEnumConversion.ToUmkaValue(enumValue.GetType(), enumValue),
            _ => throw new NotSupportedException(
                $"FromScalar<T>() does not support value type {value.GetType().FullName}. Use FromStruct<T>(), FromStaticArray<TElement>(), FromPointer(), FromHostHandle(), or an explicit From(...) overload.")
        };
    }

    /// <summary>Tries to create a scalar value from a supported managed scalar, string, pointer, enum, host handle, or existing Umka value.</summary>
    public static bool TryFromScalar<T>(T value, out UmkaValue result)
    {
        try
        {
            result = FromScalar(value);
            return true;
        }
        catch (NotSupportedException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Creates a pointer value from a runtime-owned host handle.</summary>
    public static UmkaValue FromHostHandle(UmkaHostHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return FromPointer(handle.Address);
    }

    /// <summary>Creates a fixed-size struct value for an Umka struct parameter.</summary>
    public static UmkaValue FromStruct<T>(T value) where T : struct
    {
        ValidateNoManagedReferences<T>(nameof(value));
        return new(
            UmkaValueKind.Struct,
            structuredValue: StructuredValue.CreateStruct(value, Marshal.SizeOf<T>()));
    }

    /// <summary>Tries to create a fixed-size struct value for an Umka struct parameter.</summary>
    public static bool TryFromStruct<T>(T value, out UmkaValue result) where T : struct
    {
        try
        {
            result = FromStruct(value);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Creates a fixed-size static array value for an Umka static array parameter.</summary>
    public static UmkaValue FromStaticArray<TElement>(params TElement[] values) where TElement : struct
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromStaticArray((ReadOnlySpan<TElement>)values);
    }

    /// <summary>Creates a fixed-size static array value for an Umka static array parameter.</summary>
    public static UmkaValue FromStaticArray<TElement>(ReadOnlySpan<TElement> values) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>(nameof(values));
        return new(
            UmkaValueKind.StaticArray,
            structuredValue: StructuredValue.CreateStaticArray(values, Marshal.SizeOf<TElement>()));
    }

    /// <summary>Creates a fixed-size static array value for an Umka static array parameter.</summary>
    public static UmkaValue FromStaticArray<TElement>(Span<TElement> values) where TElement : struct =>
        FromStaticArray((ReadOnlySpan<TElement>)values);

    /// <summary>Creates a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray<TElement>(params TElement[] values) where TElement : struct
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromDynamicArray((ReadOnlySpan<TElement>)values);
    }

    /// <summary>Creates a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray<TElement>(ReadOnlySpan<TElement> values) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>(nameof(values));
        return new(
            UmkaValueKind.DynamicArray,
            structuredValue: StructuredValue.CreateDynamicArray(values, Marshal.SizeOf<TElement>()));
    }

    /// <summary>Creates a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray<TElement>(Span<TElement> values) where TElement : struct =>
        FromDynamicArray((ReadOnlySpan<TElement>)values);

    /// <summary>Creates a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray(params string?[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromDynamicArray((ReadOnlySpan<string?>)values);
    }

    /// <summary>Creates a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray(ReadOnlySpan<string?> values) =>
        new(
            UmkaValueKind.DynamicArray,
            structuredValue: StructuredValue.CreateStringDynamicArray(values));

    /// <summary>Creates a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static UmkaValue FromDynamicArray(Span<string?> values) =>
        FromDynamicArray((ReadOnlySpan<string?>)values);

    /// <summary>Creates a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray<TElement>(params TElement[][] values) where TElement : struct
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromNestedDynamicArray((ReadOnlySpan<TElement[]>)values);
    }

    /// <summary>Creates a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray<TElement>(ReadOnlySpan<TElement[]> values) where TElement : struct
    {
        ValidateNoManagedReferences<TElement>(nameof(values));
        return new(
            UmkaValueKind.DynamicArray,
            structuredValue: StructuredValue.CreateNestedDynamicArray(values, Marshal.SizeOf<TElement>()));
    }

    /// <summary>Creates a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray<TElement>(Span<TElement[]> values) where TElement : struct =>
        FromNestedDynamicArray((ReadOnlySpan<TElement[]>)values);

    /// <summary>Creates a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray(params string?[][] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return FromNestedDynamicArray((ReadOnlySpan<string?[]>)values);
    }

    /// <summary>Creates a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray(ReadOnlySpan<string?[]> values) =>
        new(
            UmkaValueKind.DynamicArray,
            structuredValue: StructuredValue.CreateNestedStringDynamicArray(values));

    /// <summary>Creates a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static UmkaValue FromNestedDynamicArray(Span<string?[]> values) =>
        FromNestedDynamicArray((ReadOnlySpan<string?[]>)values);

    internal static UmkaValue FromRawDynamicArray(byte[] bytes, int length, int elementSize)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(elementSize);
        if (bytes.Length != checked(length * elementSize))
            throw new ArgumentException("Raw dynamic-array data length must match length times element size.", nameof(bytes));

        return new(
            UmkaValueKind.DynamicArray,
            structuredValue: StructuredValue.CreateRawDynamicArray(bytes, length, elementSize));
    }

    /// <summary>Tries to create a fixed-size static array value for an Umka static array parameter.</summary>
    public static bool TryFromStaticArray<TElement>(TElement[]? values, out UmkaValue result) where TElement : struct
    {
        if (values is null)
        {
            result = default;
            return false;
        }

        return TryFromStaticArray((ReadOnlySpan<TElement>)values, out result);
    }

    /// <summary>Tries to create a fixed-size static array value for an Umka static array parameter.</summary>
    public static bool TryFromStaticArray<TElement>(Span<TElement> values, out UmkaValue result) where TElement : struct =>
        TryFromStaticArray((ReadOnlySpan<TElement>)values, out result);

    /// <summary>Tries to create a fixed-size static array value for an Umka static array parameter.</summary>
    public static bool TryFromStaticArray<TElement>(ReadOnlySpan<TElement> values, out UmkaValue result) where TElement : struct
    {
        try
        {
            result = FromStaticArray(values);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Tries to create a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static bool TryFromDynamicArray<TElement>(TElement[]? values, out UmkaValue result) where TElement : struct
    {
        if (values is null)
        {
            result = default;
            return false;
        }

        return TryFromDynamicArray((ReadOnlySpan<TElement>)values, out result);
    }

    /// <summary>Tries to create a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static bool TryFromDynamicArray<TElement>(Span<TElement> values, out UmkaValue result) where TElement : struct =>
        TryFromDynamicArray((ReadOnlySpan<TElement>)values, out result);

    /// <summary>Tries to create a dynamic array value for an Umka dynamic array parameter or callback result.</summary>
    public static bool TryFromDynamicArray<TElement>(ReadOnlySpan<TElement> values, out UmkaValue result) where TElement : struct
    {
        try
        {
            result = FromDynamicArray(values);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Tries to create a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static bool TryFromDynamicArray(string?[]? values, out UmkaValue result)
    {
        if (values is null)
        {
            result = default;
            return false;
        }

        return TryFromDynamicArray((ReadOnlySpan<string?>)values, out result);
    }

    /// <summary>Tries to create a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static bool TryFromDynamicArray(Span<string?> values, out UmkaValue result) =>
        TryFromDynamicArray((ReadOnlySpan<string?>)values, out result);

    /// <summary>Tries to create a string dynamic array value for an Umka <c>[]str</c> parameter or callback result.</summary>
    public static bool TryFromDynamicArray(ReadOnlySpan<string?> values, out UmkaValue result)
    {
        try
        {
            result = FromDynamicArray(values);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Tries to create a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray<TElement>(TElement[][]? values, out UmkaValue result)
        where TElement : struct
    {
        if (values is null)
        {
            result = default;
            return false;
        }

        return TryFromNestedDynamicArray((ReadOnlySpan<TElement[]>)values, out result);
    }

    /// <summary>Tries to create a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray<TElement>(Span<TElement[]> values, out UmkaValue result)
        where TElement : struct =>
        TryFromNestedDynamicArray((ReadOnlySpan<TElement[]>)values, out result);

    /// <summary>Tries to create a nested dynamic array value for an Umka <c>[][]T</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray<TElement>(ReadOnlySpan<TElement[]> values, out UmkaValue result)
        where TElement : struct
    {
        try
        {
            result = FromNestedDynamicArray(values);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Tries to create a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray(string?[][]? values, out UmkaValue result)
    {
        if (values is null)
        {
            result = default;
            return false;
        }

        return TryFromNestedDynamicArray((ReadOnlySpan<string?[]>)values, out result);
    }

    /// <summary>Tries to create a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray(Span<string?[]> values, out UmkaValue result) =>
        TryFromNestedDynamicArray((ReadOnlySpan<string?[]>)values, out result);

    /// <summary>Tries to create a nested string dynamic array value for an Umka <c>[][]str</c> parameter or callback result.</summary>
    public static bool TryFromNestedDynamicArray(ReadOnlySpan<string?[]> values, out UmkaValue result)
    {
        try
        {
            result = FromNestedDynamicArray(values);
            return true;
        }
        catch (ArgumentException)
        {
        }
        catch (OverflowException)
        {
        }

        result = default;
        return false;
    }

    /// <summary>Reads the value as a signed integer.</summary>
    public long AsInt64() => Kind == UmkaValueKind.Int ? _int64Value : throw WrongKind(nameof(AsInt64));

    /// <summary>Reads the value as an 8-bit signed integer.</summary>
    public sbyte AsSByte() => Kind == UmkaValueKind.Int ? checked((sbyte)_int64Value) : throw WrongKind(nameof(AsSByte));

    /// <summary>Reads the value as a 16-bit signed integer.</summary>
    public short AsInt16() => Kind == UmkaValueKind.Int ? checked((short)_int64Value) : throw WrongKind(nameof(AsInt16));

    /// <summary>Reads the value as a 32-bit signed integer.</summary>
    public int AsInt32() => Kind == UmkaValueKind.Int ? checked((int)_int64Value) : throw WrongKind(nameof(AsInt32));

    /// <summary>Reads the value as an unsigned integer.</summary>
    public ulong AsUInt64() => Kind == UmkaValueKind.UInt ? _uint64Value : throw WrongKind(nameof(AsUInt64));

    /// <summary>Reads the value as an 8-bit unsigned integer.</summary>
    public byte AsByte() => Kind == UmkaValueKind.UInt ? checked((byte)_uint64Value) : throw WrongKind(nameof(AsByte));

    /// <summary>Reads the value as a 16-bit unsigned integer.</summary>
    public ushort AsUInt16() => Kind == UmkaValueKind.UInt ? checked((ushort)_uint64Value) : throw WrongKind(nameof(AsUInt16));

    /// <summary>Reads the value as a 32-bit unsigned integer.</summary>
    public uint AsUInt32() => Kind == UmkaValueKind.UInt ? checked((uint)_uint64Value) : throw WrongKind(nameof(AsUInt32));

    /// <summary>Reads the value as an Umka character.</summary>
    public char AsChar()
    {
        var value = Kind switch
        {
            UmkaValueKind.Int when _int64Value is >= byte.MinValue and <= byte.MaxValue => (ulong)_int64Value,
            UmkaValueKind.Int => throw new OverflowException(
                $"Value {_int64Value} is outside the Umka char range {byte.MinValue}..{byte.MaxValue}."),
            UmkaValueKind.UInt when _uint64Value <= byte.MaxValue => _uint64Value,
            UmkaValueKind.UInt => throw new OverflowException(
                $"Value {_uint64Value} is outside the Umka char range {byte.MinValue}..{byte.MaxValue}."),
            _ => throw WrongKind(nameof(AsChar))
        };

        return (char)value;
    }

    /// <summary>Reads the value as a real number.</summary>
    public double AsDouble() => Kind == UmkaValueKind.Real ? _doubleValue : throw WrongKind(nameof(AsDouble));

    /// <summary>Reads the value as an enum through the enum's underlying signed or unsigned storage.</summary>
    public TEnum AsEnum<TEnum>() where TEnum : struct, Enum =>
        UmkaEnumConversion.IsUnsigned<TEnum>()
            ? Kind == UmkaValueKind.UInt ? UmkaEnumConversion.ToEnum<TEnum>(_uint64Value) : throw WrongKind(nameof(AsEnum))
            : Kind == UmkaValueKind.Int ? UmkaEnumConversion.ToEnum<TEnum>(_int64Value) : throw WrongKind(nameof(AsEnum));

    /// <summary>Tries to read the value as an enum through the enum's underlying signed or unsigned storage.</summary>
    public bool TryAsEnum<TEnum>(out TEnum value) where TEnum : struct, Enum
    {
        try
        {
            value = AsEnum<TEnum>();
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

    /// <summary>Reads the value as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    [return: MaybeNull]
    public T AsScalar<T>()
    {
        var targetType = typeof(T);
        if (targetType == typeof(UmkaValue))
            return BoxScalar<T>(this);
        if (targetType == typeof(sbyte))
            return BoxScalar<T>(AsSByte());
        if (targetType == typeof(short))
            return BoxScalar<T>(AsInt16());
        if (targetType == typeof(int))
            return BoxScalar<T>(AsInt32());
        if (targetType == typeof(long))
            return BoxScalar<T>(AsInt64());
        if (targetType == typeof(byte))
            return BoxScalar<T>(AsByte());
        if (targetType == typeof(ushort))
            return BoxScalar<T>(AsUInt16());
        if (targetType == typeof(uint))
            return BoxScalar<T>(AsUInt32());
        if (targetType == typeof(ulong))
            return BoxScalar<T>(AsUInt64());
        if (targetType == typeof(float))
            return BoxScalar<T>(AsSingle());
        if (targetType == typeof(double))
            return BoxScalar<T>(AsDouble());
        if (targetType == typeof(bool))
            return BoxScalar<T>(AsBoolean());
        if (targetType == typeof(char))
            return BoxScalar<T>(AsChar());
        if (targetType == typeof(string))
            return BoxScalar<T>(AsString());
        if (targetType == typeof(IntPtr))
            return BoxScalar<T>(AsPointer());
        if (targetType.IsEnum)
            return AsEnumScalar<T>();

        throw new NotSupportedException(
            $"AsScalar<T>() does not support value type {targetType.FullName}. Use AsStruct<T>(), AsStaticArray<TElement>(), or an explicit As* reader.");
    }

    /// <summary>Tries to read the value as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool TryAsScalar<T>([MaybeNull] out T value)
    {
        try
        {
            value = AsScalar<T>();
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

    /// <summary>Reads the value as a single-precision real number.</summary>
    public float AsSingle() =>
        Kind == UmkaValueKind.Real
            ? UmkaSingleConversion.ToSingleChecked(_doubleValue, "Value")
            : throw WrongKind(nameof(AsSingle));

    /// <summary>Reads the value as a Boolean.</summary>
    public bool AsBoolean() => Kind == UmkaValueKind.Bool ? _int64Value != 0 : throw WrongKind(nameof(AsBoolean));

    /// <summary>Reads the value as a string.</summary>
    public string? AsString() => Kind == UmkaValueKind.String ? _stringValue : throw WrongKind(nameof(AsString));

    /// <summary>Reads the value as a pointer.</summary>
    public IntPtr AsPointer() => Kind == UmkaValueKind.Pointer ? _pointerValue : throw WrongKind(nameof(AsPointer));

    /// <summary>Reads the value as an opaque Umka weak pointer handle.</summary>
    public ulong AsWeakPointer() =>
        Kind == UmkaValueKind.WeakPointer ? _uint64Value : throw WrongKind(nameof(AsWeakPointer));

    /// <summary>Tries to read the value as an opaque Umka weak pointer handle.</summary>
    public bool TryAsWeakPointer(out ulong value)
    {
        if (Kind == UmkaValueKind.WeakPointer)
        {
            value = _uint64Value;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Reads the value as the original fixed-layout struct type.</summary>
    public T AsStruct<T>() where T : struct
    {
        ValidateNoManagedReferences<T>();
        return Kind == UmkaValueKind.Struct
            ? _structuredValue!.GetStruct<T>()
            : throw WrongKind(nameof(AsStruct));
    }

    /// <summary>Tries to read the value as the original fixed-layout struct type.</summary>
    public bool TryAsStruct<T>(out T value) where T : struct
    {
        try
        {
            value = AsStruct<T>();
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

    /// <summary>Reads the value as a defensive copy of the original fixed-layout static array type.</summary>
    public TElement[] AsStaticArray<TElement>() where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        return Kind == UmkaValueKind.StaticArray
            ? _structuredValue!.GetStaticArray<TElement>()
            : throw WrongKind(nameof(AsStaticArray));
    }

    /// <summary>Reads the value as a defensive copy of the original dynamic array type.</summary>
    public TElement[] AsDynamicArray<TElement>() where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        return Kind == UmkaValueKind.DynamicArray
            ? _structuredValue!.GetDynamicArray<TElement>()
            : throw WrongKind(nameof(AsDynamicArray));
    }

    /// <summary>Reads the value as a defensive copy of the original string dynamic array.</summary>
    public string?[] AsStringArray() =>
        Kind == UmkaValueKind.DynamicArray
            ? _structuredValue!.GetStringArray()
            : throw WrongKind(nameof(AsStringArray));

    /// <summary>Reads the value as a defensive copy of the original nested dynamic array type.</summary>
    public TElement[][] AsNestedDynamicArray<TElement>() where TElement : struct
    {
        ValidateNoManagedReferences<TElement>();
        return Kind == UmkaValueKind.DynamicArray
            ? _structuredValue!.GetNestedDynamicArray<TElement>()
            : throw WrongKind(nameof(AsNestedDynamicArray));
    }

    /// <summary>Reads the value as a defensive copy of the original nested string dynamic array.</summary>
    public string?[][] AsNestedStringArray() =>
        Kind == UmkaValueKind.DynamicArray
            ? _structuredValue!.GetNestedStringArray()
            : throw WrongKind(nameof(AsNestedStringArray));

    /// <summary>Tries to read the value as a defensive copy of the original fixed-layout static array type.</summary>
    public bool TryAsStaticArray<TElement>([NotNullWhen(true)] out TElement[]? value) where TElement : struct
    {
        try
        {
            value = AsStaticArray<TElement>();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Tries to read the value as a defensive copy of the original dynamic array type.</summary>
    public bool TryAsDynamicArray<TElement>([NotNullWhen(true)] out TElement[]? value) where TElement : struct
    {
        try
        {
            value = AsDynamicArray<TElement>();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Tries to read the value as a defensive copy of the original string dynamic array.</summary>
    public bool TryAsStringArray([NotNullWhen(true)] out string?[]? value)
    {
        try
        {
            value = AsStringArray();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Tries to read the value as a defensive copy of the original nested dynamic array type.</summary>
    public bool TryAsNestedDynamicArray<TElement>([NotNullWhen(true)] out TElement[][]? value) where TElement : struct
    {
        try
        {
            value = AsNestedDynamicArray<TElement>();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Tries to read the value as a defensive copy of the original nested string dynamic array.</summary>
    public bool TryAsNestedStringArray([NotNullWhen(true)] out string?[][]? value)
    {
        try
        {
            value = AsNestedStringArray();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (ArgumentException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Returns a diagnostic string that describes the stored Umka value kind and payload.</summary>
    public override string ToString() =>
        Kind switch
        {
            UmkaValueKind.Void => "UmkaValue(Void)",
            UmkaValueKind.Int => $"UmkaValue(Int: {_int64Value.ToString(CultureInfo.InvariantCulture)})",
            UmkaValueKind.UInt => $"UmkaValue(UInt: {_uint64Value.ToString(CultureInfo.InvariantCulture)})",
            UmkaValueKind.Real => $"UmkaValue(Real: {UmkaSingleConversion.Format(_doubleValue)})",
            UmkaValueKind.Bool => $"UmkaValue(Bool: {(_int64Value != 0).ToString(CultureInfo.InvariantCulture)})",
            UmkaValueKind.String => _stringValue is null
                ? "UmkaValue(String: null)"
                : $"UmkaValue(String: \"{EscapeString(_stringValue)}\")",
            UmkaValueKind.Pointer => $"UmkaValue(Pointer: {FormatPointer(_pointerValue)})",
            UmkaValueKind.WeakPointer => $"UmkaValue(WeakPointer: 0x{_uint64Value:x})",
            UmkaValueKind.StaticArray or UmkaValueKind.Struct or UmkaValueKind.DynamicArray => _structuredValue?.ToString() ?? $"UmkaValue({Kind})",
            _ => $"UmkaValue({Kind})"
        };

    internal int StructuredSize => _structuredValue?.Size ?? throw WrongKind(nameof(StructuredSize));

    internal int StructuredLength => _structuredValue?.Length ?? throw WrongKind(nameof(StructuredLength));

    internal int StructuredElementSize => _structuredValue?.ElementSize ?? throw WrongKind(nameof(StructuredElementSize));

    internal bool IsStringDynamicArray =>
        Kind == UmkaValueKind.DynamicArray && _structuredValue?.IsStringArray == true;

    internal bool IsNestedDynamicArray =>
        Kind == UmkaValueKind.DynamicArray && _structuredValue?.IsNestedArray == true;

    internal bool IsNestedStringDynamicArray =>
        Kind == UmkaValueKind.DynamicArray && _structuredValue?.IsNestedStringArray == true;

    internal string?[] GetStringDynamicArray() =>
        IsStringDynamicArray ? _structuredValue!.GetStringArray() : throw WrongKind(nameof(GetStringDynamicArray));

    internal int[] GetNestedDynamicArrayRowLengths() =>
        IsNestedDynamicArray ? _structuredValue!.GetNestedRowLengths() : throw WrongKind(nameof(GetNestedDynamicArrayRowLengths));

    internal string?[] GetFlattenedNestedStringDynamicArray() =>
        IsNestedStringDynamicArray ? _structuredValue!.GetFlattenedNestedStringArray() : throw WrongKind(nameof(GetFlattenedNestedStringDynamicArray));

    internal void CopyStructuredTo(IntPtr destination)
    {
        if (_structuredValue is null)
            throw WrongKind(nameof(CopyStructuredTo));

        _structuredValue.CopyTo(destination);
    }

    internal void CopyNestedDynamicArrayElementsTo(IntPtr destination)
    {
        if (!IsNestedDynamicArray)
            throw WrongKind(nameof(CopyNestedDynamicArrayElementsTo));

        _structuredValue!.CopyNestedElementsTo(destination);
    }

    private static void ValidateNoManagedReferences<T>(string parameterName) where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException(
                $"Managed type {typeof(T).FullName} contains managed references and cannot be copied into Umka aggregate storage.",
                parameterName);
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

    private InvalidOperationException WrongKind(string member) =>
        new($"{member} cannot read a value of kind {Kind}.");

    private static T BoxScalar<T>(object? value) => (T)value!;

    private T AsEnumScalar<T>()
    {
        var enumType = typeof(T);
        object rawValue = Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => Kind == UmkaValueKind.Int ? checked((sbyte)_int64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.Int16 => Kind == UmkaValueKind.Int ? checked((short)_int64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.Int32 => Kind == UmkaValueKind.Int ? checked((int)_int64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.Int64 => Kind == UmkaValueKind.Int ? _int64Value : throw WrongKind(nameof(AsScalar)),
            TypeCode.Byte => Kind == UmkaValueKind.UInt ? checked((byte)_uint64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.UInt16 => Kind == UmkaValueKind.UInt ? checked((ushort)_uint64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.UInt32 => Kind == UmkaValueKind.UInt ? checked((uint)_uint64Value) : throw WrongKind(nameof(AsScalar)),
            TypeCode.UInt64 => Kind == UmkaValueKind.UInt ? _uint64Value : throw WrongKind(nameof(AsScalar)),
            _ => throw new InvalidOperationException($"Enum type {enumType.FullName} has an unsupported underlying storage type.")
        };

        return (T)Enum.ToObject(enumType, rawValue);
    }

    private static string FormatPointer(IntPtr value) =>
        $"0x{value.ToInt64().ToString("X", CultureInfo.InvariantCulture)}";

    private static string EscapeString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private sealed class StructuredValue
    {
        private readonly object _value;
        private readonly int _elementSize;
        private readonly bool _rawBytes;
        private readonly bool _stringArray;
        private readonly bool _nestedArray;
        private readonly bool _nestedStringArray;

        private StructuredValue(
            UmkaValueKind kind,
            object value,
            int size,
            int length,
            int elementSize,
            bool rawBytes = false,
            bool stringArray = false,
            bool nestedArray = false,
            bool nestedStringArray = false)
        {
            Kind = kind;
            _value = value;
            Size = size;
            Length = length;
            _elementSize = elementSize;
            _rawBytes = rawBytes;
            _stringArray = stringArray;
            _nestedArray = nestedArray;
            _nestedStringArray = nestedStringArray;
        }

        public UmkaValueKind Kind { get; }

        public object Value =>
            _value is Array array
                ? _nestedArray ? CloneNestedArray(array) : array.Clone()
                : _value;

        public int Size { get; }

        public int Length { get; }

        public int ElementSize => _elementSize;

        public bool IsStringArray => _stringArray;

        public bool IsNestedArray => _nestedArray;

        public bool IsNestedStringArray => _nestedStringArray;

        public static StructuredValue CreateStruct<T>(T value, int size) where T : struct =>
            new(UmkaValueKind.Struct, value, size, length: 1, elementSize: size);

        public static StructuredValue CreateStaticArray<TElement>(ReadOnlySpan<TElement> values, int elementSize)
            where TElement : struct
        {
            var copy = values.ToArray();
            return new(
                UmkaValueKind.StaticArray,
                copy,
                checked(elementSize * copy.Length),
                copy.Length,
                elementSize);
        }

        public static StructuredValue CreateDynamicArray<TElement>(ReadOnlySpan<TElement> values, int elementSize)
            where TElement : struct
        {
            var copy = values.ToArray();
            return new(
                UmkaValueKind.DynamicArray,
                copy,
                checked(elementSize * copy.Length),
                copy.Length,
                elementSize);
        }

        public static StructuredValue CreateStringDynamicArray(ReadOnlySpan<string?> values)
        {
            var copy = values.ToArray();
            for (var i = 0; i < copy.Length; i++)
                UmkaStringValidation.ThrowIfContainsNullCharacter(copy[i], nameof(values));

            return new(
                UmkaValueKind.DynamicArray,
                copy,
                checked(IntPtr.Size * copy.Length),
                copy.Length,
                IntPtr.Size,
                stringArray: true);
        }

        public static StructuredValue CreateNestedDynamicArray<TElement>(ReadOnlySpan<TElement[]> values, int elementSize)
            where TElement : struct
        {
            var copy = new TElement[values.Length][];
            var totalLength = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is null)
                    throw new ArgumentException("Nested dynamic-array rows cannot be null.", nameof(values));

                copy[i] = values[i].ToArray();
                totalLength = checked(totalLength + copy[i].Length);
            }

            return new(
                UmkaValueKind.DynamicArray,
                copy,
                checked(elementSize * totalLength),
                copy.Length,
                elementSize,
                nestedArray: true);
        }

        public static StructuredValue CreateNestedStringDynamicArray(ReadOnlySpan<string?[]> values)
        {
            var copy = new string?[values.Length][];
            var totalLength = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is null)
                    throw new ArgumentException("Nested dynamic-array rows cannot be null.", nameof(values));

                copy[i] = values[i].ToArray();
                totalLength = checked(totalLength + copy[i].Length);
                for (var j = 0; j < copy[i].Length; j++)
                    UmkaStringValidation.ThrowIfContainsNullCharacter(copy[i][j], nameof(values));
            }

            return new(
                UmkaValueKind.DynamicArray,
                copy,
                checked(IntPtr.Size * totalLength),
                copy.Length,
                IntPtr.Size,
                nestedArray: true,
                nestedStringArray: true);
        }

        public static StructuredValue CreateRawDynamicArray(byte[] bytes, int length, int elementSize)
        {
            var copy = bytes.ToArray();
            return new(
                UmkaValueKind.DynamicArray,
                copy,
                copy.Length,
                length,
                elementSize,
                rawBytes: true);
        }

        public T GetStruct<T>() where T : struct =>
            _value is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"Structured value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(T).FullName}.");

        public TElement[] GetStaticArray<TElement>() where TElement : struct =>
            _value is TElement[] typed
                ? typed.ToArray()
                : throw new InvalidOperationException(
                    $"Static array value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(TElement[]).FullName}.");

        public TElement[] GetDynamicArray<TElement>() where TElement : struct =>
            _value is TElement[] typed
                ? typed.ToArray()
                : throw new InvalidOperationException(
                    $"Dynamic array value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(TElement[]).FullName}.");

        public string?[] GetStringArray() =>
            _value is string?[] typed
                ? typed.ToArray()
                : throw new InvalidOperationException(
                    $"Dynamic array value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(string[]).FullName}.");

        public TElement[][] GetNestedDynamicArray<TElement>() where TElement : struct =>
            _value is TElement[][] typed
                ? CloneNestedArray(typed)
                : throw new InvalidOperationException(
                    $"Nested dynamic array value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(TElement[][]).FullName}.");

        public string?[][] GetNestedStringArray() =>
            _value is string?[][] typed
                ? CloneNestedArray(typed)
                : throw new InvalidOperationException(
                    $"Nested dynamic array value stores managed type {_value.GetType().FullName} and cannot be read as {typeof(string[][]).FullName}.");

        public int[] GetNestedRowLengths()
        {
            if (!_nestedArray || _value is not Array rows)
                throw new InvalidOperationException("Value is not a nested dynamic array.");

            var lengths = new int[rows.Length];
            for (var i = 0; i < rows.Length; i++)
                lengths[i] = ((Array)rows.GetValue(i)!).Length;

            return lengths;
        }

        public string?[] GetFlattenedNestedStringArray()
        {
            if (!_nestedStringArray || _value is not string?[][] rows)
                throw new InvalidOperationException("Value is not a nested string dynamic array.");

            var totalLength = 0;
            for (var i = 0; i < rows.Length; i++)
                totalLength = checked(totalLength + rows[i].Length);

            var result = new string?[totalLength];
            var offset = 0;
            for (var i = 0; i < rows.Length; i++)
            {
                Array.Copy(rows[i], 0, result, offset, rows[i].Length);
                offset += rows[i].Length;
            }

            return result;
        }

        public void CopyTo(IntPtr destination)
        {
            if (_nestedArray)
                throw new InvalidOperationException("Nested dynamic array values cannot be copied as unmanaged bytes.");

            if (_stringArray)
                throw new InvalidOperationException("String dynamic array values cannot be copied as unmanaged bytes.");

            if (_rawBytes)
            {
                Marshal.Copy((byte[])_value, 0, destination, Size);
                return;
            }

            if (Kind is UmkaValueKind.StaticArray or UmkaValueKind.DynamicArray)
            {
                var values = (Array)_value;
                for (var i = 0; i < values.Length; i++)
                    Marshal.StructureToPtr(values.GetValue(i)!, IntPtr.Add(destination, i * _elementSize), fDeleteOld: false);
                return;
            }

            Marshal.StructureToPtr(_value, destination, fDeleteOld: false);
        }

        public void CopyNestedElementsTo(IntPtr destination)
        {
            if (!_nestedArray || _nestedStringArray)
                throw new InvalidOperationException("Value is not a fixed-layout nested dynamic array.");

            var rows = (Array)_value;
            var offset = 0;
            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                var row = (Array)rows.GetValue(rowIndex)!;
                for (var elementIndex = 0; elementIndex < row.Length; elementIndex++)
                {
                    Marshal.StructureToPtr(
                        row.GetValue(elementIndex)!,
                        IntPtr.Add(destination, offset),
                        fDeleteOld: false);
                    offset += _elementSize;
                }
            }
        }

        public override string ToString() =>
            Kind switch
            {
                UmkaValueKind.StaticArray => $"UmkaValue(StaticArray: Length={Length}, Size={Size})",
                UmkaValueKind.DynamicArray when _nestedArray => $"UmkaValue(NestedDynamicArray: Length={Length}, Size={Size})",
                UmkaValueKind.DynamicArray => $"UmkaValue(DynamicArray: Length={Length}, Size={Size})",
                _ => $"UmkaValue(Struct: Size={Size})"
            };

        private static Array CloneNestedArray(Array array)
        {
            var clone = (Array)array.Clone();
            for (var i = 0; i < clone.Length; i++)
            {
                if (clone.GetValue(i) is Array row)
                    clone.SetValue(row.Clone(), i);
            }

            return clone;
        }

        private static TElement[][] CloneNestedArray<TElement>(TElement[][] values)
        {
            var copy = new TElement[values.Length][];
            for (var i = 0; i < values.Length; i++)
                copy[i] = values[i].ToArray();

            return copy;
        }
    }
}
