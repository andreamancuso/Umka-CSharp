namespace UmkaSharp;

/// <summary>Represents an Umka dynamic <c>any</c> value.</summary>
public sealed class UmkaAnyValue
{
    private static readonly UmkaTypeInfo IntType = new(UmkaTypeKind.SignedInteger, "int") { NativeSize = sizeof(long) };
    private static readonly UmkaTypeInfo UIntType = new(UmkaTypeKind.UnsignedInteger, "uint") { NativeSize = sizeof(ulong) };
    private static readonly UmkaTypeInfo CharType = new(UmkaTypeKind.Character, "char") { NativeSize = sizeof(byte) };
    private static readonly UmkaTypeInfo RealType = new(UmkaTypeKind.Real, "real") { NativeSize = sizeof(double) };
    private static readonly UmkaTypeInfo BoolType = new(UmkaTypeKind.Boolean, "bool") { NativeSize = sizeof(byte) };
    private static readonly UmkaTypeInfo StringType = new(UmkaTypeKind.String, "str") { NativeSize = IntPtr.Size, HasReferences = true };

    private UmkaAnyValue(bool isNull, UmkaTypeInfo? payloadType, UmkaValue payload)
    {
        IsNull = isNull;
        PayloadType = payloadType;
        Payload = payload;
    }

    /// <summary>Gets an Umka null <c>any</c> value.</summary>
    public static UmkaAnyValue Null { get; } = new(isNull: true, payloadType: null, UmkaValue.Void);

    /// <summary>Gets a value indicating whether this value is a null <c>any</c>.</summary>
    public bool IsNull { get; }

    /// <summary>Gets the concrete payload type, or <see langword="null" /> for a null <c>any</c>.</summary>
    public UmkaTypeInfo? PayloadType { get; }

    /// <summary>Gets the concrete payload value, or <see cref="UmkaValue.Void" /> for a null <c>any</c>.</summary>
    public UmkaValue Payload { get; }

    /// <summary>Creates an <c>any</c> value containing a signed integer payload.</summary>
    public static UmkaAnyValue From(sbyte value) => From((long)value);

    /// <summary>Creates an <c>any</c> value containing a signed integer payload.</summary>
    public static UmkaAnyValue From(short value) => From((long)value);

    /// <summary>Creates an <c>any</c> value containing a signed integer payload.</summary>
    public static UmkaAnyValue From(int value) => From((long)value);

    /// <summary>Creates an <c>any</c> value containing a signed integer payload.</summary>
    public static UmkaAnyValue From(long value) => new(isNull: false, IntType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing an unsigned integer payload.</summary>
    public static UmkaAnyValue From(byte value) => From((ulong)value);

    /// <summary>Creates an <c>any</c> value containing an unsigned integer payload.</summary>
    public static UmkaAnyValue From(ushort value) => From((ulong)value);

    /// <summary>Creates an <c>any</c> value containing an unsigned integer payload.</summary>
    public static UmkaAnyValue From(uint value) => From((ulong)value);

    /// <summary>Creates an <c>any</c> value containing an unsigned integer payload.</summary>
    public static UmkaAnyValue From(ulong value) => new(isNull: false, UIntType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing a character payload.</summary>
    public static UmkaAnyValue From(char value) => new(isNull: false, CharType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing a real payload.</summary>
    public static UmkaAnyValue From(float value) => From((double)value);

    /// <summary>Creates an <c>any</c> value containing a real payload.</summary>
    public static UmkaAnyValue From(double value) => new(isNull: false, RealType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing a Boolean payload.</summary>
    public static UmkaAnyValue From(bool value) => new(isNull: false, BoolType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing a string payload.</summary>
    public static UmkaAnyValue From(string? value) => new(isNull: false, StringType, UmkaValue.From(value));

    /// <summary>Creates an <c>any</c> value containing a retained native Umka payload.</summary>
    public static UmkaAnyValue From(UmkaNativeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new UmkaAnyValue(isNull: false, value.Type, UmkaValue.FromNativeValue(value));
    }

    /// <summary>Creates an <c>any</c> value from a supported dynamic payload.</summary>
    public static UmkaAnyValue From(UmkaValue payload) =>
        payload.Kind switch
        {
            UmkaValueKind.Int => new UmkaAnyValue(isNull: false, IntType, payload),
            UmkaValueKind.UInt => new UmkaAnyValue(isNull: false, UIntType, payload),
            UmkaValueKind.Real => new UmkaAnyValue(isNull: false, RealType, payload),
            UmkaValueKind.Bool => new UmkaAnyValue(isNull: false, BoolType, payload),
            UmkaValueKind.String => new UmkaAnyValue(isNull: false, StringType, payload),
            UmkaValueKind.NativeValue => From(payload.AsNativeValue()),
            UmkaValueKind.Void => throw new NotSupportedException("Use UmkaAnyValue.Null to create a null Umka any value."),
            UmkaValueKind.StaticArray
                or UmkaValueKind.Struct
                or UmkaValueKind.DynamicArray
                or UmkaValueKind.Map => throw new NotSupportedException(
                    $"Managed {payload.Kind} values do not carry the concrete Umka type metadata required to construct an any payload. Retain an Umka value and pass it with UmkaAnyValue.From(UmkaNativeValue)."),
            _ => throw new NotSupportedException($"UmkaValue kind {payload.Kind} is not supported as an any payload.")
        };

    /// <summary>Creates an Umka value that can pass this <c>any</c> value to a function or callback result.</summary>
    public UmkaValue ToValue() => UmkaValue.FromAny(this);

    /// <summary>Returns a diagnostic string that includes the null state or concrete payload type.</summary>
    public override string ToString() =>
        IsNull ? "UmkaAnyValue(Null)" : $"UmkaAnyValue(PayloadType={PayloadType?.TypeName ?? "unknown"})";

    internal static UmkaAnyValue FromInspected(UmkaTypeInfo payloadType, UmkaValue payload)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        if (payload.Kind == UmkaValueKind.Void)
            throw new ArgumentException("Inspected non-null any payloads cannot be void.", nameof(payload));

        return new UmkaAnyValue(isNull: false, payloadType, payload);
    }
}
