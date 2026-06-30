namespace UmkaSharp;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

/// <summary>Broad managed classification of an Umka type exposed by function metadata.</summary>
#pragma warning disable CA1720 // Names intentionally mirror Umka's type model.
public enum UmkaTypeKind
{
    /// <summary>The type is unknown or unavailable.</summary>
    Unknown,
    /// <summary>The Umka `void` type.</summary>
    Void,
    /// <summary>The Umka `null` pseudo-type.</summary>
    Null,
    /// <summary>A signed integer type such as `int8`, `int16`, `int32`, or `int`.</summary>
    SignedInteger,
    /// <summary>An unsigned integer type such as `uint8`, `uint16`, `uint32`, or `uint`.</summary>
    UnsignedInteger,
    /// <summary>The Umka `bool` type.</summary>
    Boolean,
    /// <summary>The Umka `char` type.</summary>
    Character,
    /// <summary>A real type such as `real32` or `real`.</summary>
    Real,
    /// <summary>An Umka pointer type.</summary>
    Pointer,
    /// <summary>An Umka weak pointer type.</summary>
    WeakPointer,
    /// <summary>An Umka static array type.</summary>
    StaticArray,
    /// <summary>An Umka dynamic array type.</summary>
    DynamicArray,
    /// <summary>The Umka `str` type.</summary>
    String,
    /// <summary>An Umka map type.</summary>
    Map,
    /// <summary>An Umka struct type.</summary>
    Struct,
    /// <summary>An Umka interface type.</summary>
    Interface,
    /// <summary>An Umka closure type.</summary>
    Closure,
    /// <summary>An Umka fiber type.</summary>
    Fiber,
    /// <summary>An Umka function type.</summary>
    Function
}
#pragma warning restore CA1720

/// <summary>Describes a parameter or result type of a resolved Umka function.</summary>
public sealed record UmkaTypeInfo(UmkaTypeKind Kind, string TypeName)
{
    private readonly string _typeName = ValidateTypeName(TypeName, nameof(TypeName));
    private readonly int _nativeSize;
    private readonly int _itemCount;

    /// <summary>Gets the broad managed type classification.</summary>
    public UmkaTypeKind Kind { get; init; } = Kind;

    /// <summary>Gets the Umka type name reported by native metadata, or <c>unknown</c> when unavailable.</summary>
    public string TypeName
    {
        get => _typeName;
        init => _typeName = ValidateTypeName(value, nameof(value));
    }

    /// <summary>Gets the native Umka storage size in bytes, or <c>0</c> when unavailable.</summary>
    public int NativeSize
    {
        get => _nativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _nativeSize = value;
        }
    }

    /// <summary>Gets the native Umka item count, such as a static array length, or <c>0</c> when unavailable.</summary>
    public int ItemCount
    {
        get => _itemCount;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _itemCount = value;
        }
    }

    /// <summary>Gets a value indicating whether the Umka type contains Umka-managed references.</summary>
    public bool HasReferences { get; init; }

    /// <summary>Gets a value indicating whether the type is a scalar value kind with explicit scalar readers.</summary>
    public bool IsScalar =>
        Kind is UmkaTypeKind.SignedInteger
            or UmkaTypeKind.UnsignedInteger
            or UmkaTypeKind.Boolean
            or UmkaTypeKind.Character
            or UmkaTypeKind.Real;

    /// <summary>Gets a value indicating whether the type is a fixed-layout aggregate shape.</summary>
    public bool IsAggregate =>
        Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct;

    /// <summary>Gets a value indicating whether the type currently needs a future managed wrapper before crossing the boundary.</summary>
    public bool IsDeferred =>
        Kind is UmkaTypeKind.WeakPointer
            or UmkaTypeKind.DynamicArray
            or UmkaTypeKind.Map
            or UmkaTypeKind.Interface
            or UmkaTypeKind.Closure
            or UmkaTypeKind.Fiber
            or UmkaTypeKind.Function;

    /// <summary>Returns whether the type can be represented as a dynamic <see cref="UmkaValue" />.</summary>
    public bool CanReadAsValue() =>
        Kind is UmkaTypeKind.Void
            or UmkaTypeKind.SignedInteger
            or UmkaTypeKind.UnsignedInteger
            or UmkaTypeKind.Real
            or UmkaTypeKind.Boolean
            or UmkaTypeKind.Character
            or UmkaTypeKind.String
            or UmkaTypeKind.Pointer;

    /// <summary>Returns whether the type can be read as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool CanReadAsScalar<T>() => CanReadAsScalarType(typeof(T));

    /// <summary>Returns whether the type can be read into the managed struct type.</summary>
    public bool CanReadAsStruct<T>() where T : struct =>
        Kind == UmkaTypeKind.Struct && CanReadAsFixedLayout<T>();

    /// <summary>Returns whether the type can be read into the managed fixed-layout type.</summary>
    public bool CanReadAsFixedLayout<T>() where T : struct =>
        (Kind is UmkaTypeKind.StaticArray or UmkaTypeKind.Struct)
            && !HasReferences
            && NativeSize > 0
            && TryGetMarshalSize<T>(out var managedSize)
            && managedSize == NativeSize;

    /// <summary>Returns whether the type can be read into a managed array of the given length.</summary>
    public bool CanReadAsArray<TElement>(int length) where TElement : struct =>
        CanReadAsArray<TElement>(length, requireKnownItemCount: false);

    internal bool CanReadAsArray<TElement>(int length, bool requireKnownItemCount) where TElement : struct
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (Kind != UmkaTypeKind.StaticArray
            || HasReferences
            || NativeSize <= 0
            || requireKnownItemCount && length != ItemCount
            || !requireKnownItemCount && ItemCount > 0 && length != ItemCount
            || !TryGetMarshalSize<TElement>(out var elementSize))
            return false;

        try
        {
            return checked(elementSize * length) == NativeSize;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Deconstructs the type metadata into its broad kind and native type name.</summary>
    public void Deconstruct(out UmkaTypeKind Kind, out string TypeName)
    {
        Kind = this.Kind;
        TypeName = this.TypeName;
    }

    /// <summary>Returns a diagnostic string that summarizes the Umka type metadata.</summary>
    public override string ToString()
    {
        var details = new List<string>
        {
            $"Kind={Kind}",
            $"TypeName={TypeName}"
        };

        if (NativeSize > 0)
            details.Add($"NativeSize={NativeSize}");
        if (ItemCount > 0)
            details.Add($"ItemCount={ItemCount}");
        if (HasReferences)
            details.Add("HasReferences=True");

        return $"UmkaTypeInfo({string.Join(", ", details)})";
    }

    private static string ValidateTypeName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, parameterName);
        return value;
    }

    private bool CanReadAsScalarType(Type targetType)
    {
        if (targetType == typeof(UmkaValue))
            return CanReadAsValue();

        if (targetType == typeof(sbyte)
            || targetType == typeof(short)
            || targetType == typeof(int)
            || targetType == typeof(long))
            return Kind is UmkaTypeKind.SignedInteger or UmkaTypeKind.Character;
        if (targetType == typeof(byte)
            || targetType == typeof(ushort)
            || targetType == typeof(uint)
            || targetType == typeof(ulong))
            return Kind is UmkaTypeKind.UnsignedInteger or UmkaTypeKind.Character;
        if (targetType == typeof(float) || targetType == typeof(double))
            return Kind == UmkaTypeKind.Real;
        if (targetType == typeof(bool))
            return Kind == UmkaTypeKind.Boolean;
        if (targetType == typeof(char))
            return Kind == UmkaTypeKind.Character;
        if (targetType == typeof(string))
            return Kind == UmkaTypeKind.String;
        if (targetType == typeof(IntPtr))
            return Kind == UmkaTypeKind.Pointer;
        if (targetType.IsEnum)
        {
            return UmkaEnumConversion.IsUnsigned(targetType)
                ? Kind is UmkaTypeKind.UnsignedInteger or UmkaTypeKind.Character
                : Kind is UmkaTypeKind.SignedInteger or UmkaTypeKind.Character;
        }

        return false;
    }

    private static bool TryGetMarshalSize<T>(out int size) where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            size = 0;
            return false;
        }

        try
        {
            size = Marshal.SizeOf<T>();
            return true;
        }
        catch (ArgumentException)
        {
            size = 0;
            return false;
        }
    }
}

internal enum NativeUmkaTypeKind
{
    None = 0,
    Forward = 1,
    Void = 2,
    Null = 3,
    Int8 = 4,
    Int16 = 5,
    Int32 = 6,
    Int = 7,
    UInt8 = 8,
    UInt16 = 9,
    UInt32 = 10,
    UInt = 11,
    Bool = 12,
    Char = 13,
    Real32 = 14,
    Real = 15,
    Pointer = 16,
    WeakPointer = 17,
    StaticArray = 18,
    DynamicArray = 19,
    String = 20,
    Map = 21,
    Struct = 22,
    Interface = 23,
    Closure = 24,
    Fiber = 25,
    Function = 26
}

internal static class UmkaTypeInfoFactory
{
    public static UmkaTypeInfo Create(
        NativeUmkaTypeKind kind,
        string? typeName,
        int nativeSize = 0,
        int itemCount = 0,
        bool hasReferences = false) =>
        new(MapKind(kind), string.IsNullOrWhiteSpace(typeName) ? "unknown" : typeName)
        {
            NativeSize = Math.Max(0, nativeSize),
            ItemCount = Math.Max(0, itemCount),
            HasReferences = hasReferences
        };

    public static UmkaTypeKind MapKind(NativeUmkaTypeKind kind) =>
        kind switch
        {
            NativeUmkaTypeKind.Void => UmkaTypeKind.Void,
            NativeUmkaTypeKind.Null => UmkaTypeKind.Null,
            NativeUmkaTypeKind.Int8
                or NativeUmkaTypeKind.Int16
                or NativeUmkaTypeKind.Int32
                or NativeUmkaTypeKind.Int => UmkaTypeKind.SignedInteger,
            NativeUmkaTypeKind.UInt8
                or NativeUmkaTypeKind.UInt16
                or NativeUmkaTypeKind.UInt32
                or NativeUmkaTypeKind.UInt => UmkaTypeKind.UnsignedInteger,
            NativeUmkaTypeKind.Bool => UmkaTypeKind.Boolean,
            NativeUmkaTypeKind.Char => UmkaTypeKind.Character,
            NativeUmkaTypeKind.Real32 or NativeUmkaTypeKind.Real => UmkaTypeKind.Real,
            NativeUmkaTypeKind.Pointer => UmkaTypeKind.Pointer,
            NativeUmkaTypeKind.WeakPointer => UmkaTypeKind.WeakPointer,
            NativeUmkaTypeKind.StaticArray => UmkaTypeKind.StaticArray,
            NativeUmkaTypeKind.DynamicArray => UmkaTypeKind.DynamicArray,
            NativeUmkaTypeKind.String => UmkaTypeKind.String,
            NativeUmkaTypeKind.Map => UmkaTypeKind.Map,
            NativeUmkaTypeKind.Struct => UmkaTypeKind.Struct,
            NativeUmkaTypeKind.Interface => UmkaTypeKind.Interface,
            NativeUmkaTypeKind.Closure => UmkaTypeKind.Closure,
            NativeUmkaTypeKind.Fiber => UmkaTypeKind.Fiber,
            NativeUmkaTypeKind.Function => UmkaTypeKind.Function,
            _ => UmkaTypeKind.Unknown
        };
}
