namespace UmkaSharp;

/// <summary>Supported managed value kinds for the first UmkaSharp marshalling layer.</summary>
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
    Pointer
}

/// <summary>A simple typed value container used by callbacks and dynamic calls.</summary>
public readonly record struct UmkaValue(UmkaValueKind Kind, object? Value)
{
    /// <summary>Gets a void value.</summary>
    public static UmkaValue Void => new(UmkaValueKind.Void, null);

    /// <summary>Creates a signed integer value.</summary>
    public static UmkaValue From(long value) => new(UmkaValueKind.Int, value);

    /// <summary>Creates an unsigned integer value.</summary>
    public static UmkaValue From(ulong value) => new(UmkaValueKind.UInt, value);

    /// <summary>Creates a real value.</summary>
    public static UmkaValue From(double value) => new(UmkaValueKind.Real, value);

    /// <summary>Creates a Boolean value.</summary>
    public static UmkaValue From(bool value) => new(UmkaValueKind.Bool, value);

    /// <summary>Creates a string value.</summary>
    public static UmkaValue From(string? value) => new(UmkaValueKind.String, value);

    /// <summary>Creates a pointer value.</summary>
    public static UmkaValue FromPointer(IntPtr value) => new(UmkaValueKind.Pointer, value);

    /// <summary>Reads the value as a signed integer.</summary>
    public long AsInt64() => Kind == UmkaValueKind.Int ? (long)Value! : throw WrongKind(nameof(AsInt64));

    /// <summary>Reads the value as an unsigned integer.</summary>
    public ulong AsUInt64() => Kind == UmkaValueKind.UInt ? (ulong)Value! : throw WrongKind(nameof(AsUInt64));

    /// <summary>Reads the value as a real number.</summary>
    public double AsDouble() => Kind == UmkaValueKind.Real ? (double)Value! : throw WrongKind(nameof(AsDouble));

    /// <summary>Reads the value as a Boolean.</summary>
    public bool AsBoolean() => Kind == UmkaValueKind.Bool ? (bool)Value! : throw WrongKind(nameof(AsBoolean));

    /// <summary>Reads the value as a string.</summary>
    public string? AsString() => Kind == UmkaValueKind.String ? (string?)Value : throw WrongKind(nameof(AsString));

    /// <summary>Reads the value as a pointer.</summary>
    public IntPtr AsPointer() => Kind == UmkaValueKind.Pointer ? (IntPtr)Value! : throw WrongKind(nameof(AsPointer));

    private InvalidOperationException WrongKind(string member) =>
        new($"{member} cannot read a value of kind {Kind}.");
}
