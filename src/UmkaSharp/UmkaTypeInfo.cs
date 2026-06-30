namespace UmkaSharp;

using System.Collections.ObjectModel;
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
    private static readonly ReadOnlyCollection<UmkaEnumMemberInfo> EmptyEnumMembers =
        Array.AsReadOnly(Array.Empty<UmkaEnumMemberInfo>());

    private readonly string _typeName = ValidateTypeName(TypeName, nameof(TypeName));
    private readonly string? _elementTypeName;
    private readonly int _nativeSize;
    private readonly int _itemCount;
    private readonly int _elementNativeSize;
    private readonly string? _nestedElementTypeName;
    private readonly int _nestedElementNativeSize;
    private readonly string? _mapKeyTypeName;
    private readonly string? _mapValueTypeName;
    private readonly int _mapKeyNativeSize;
    private readonly int _mapValueNativeSize;
    private readonly string? _mapValueElementTypeName;
    private readonly int _mapValueElementNativeSize;
    private readonly ReadOnlyCollection<UmkaEnumMemberInfo> _enumMembers = EmptyEnumMembers;

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

    /// <summary>Gets the broad managed classification of the element type for array-like Umka types.</summary>
    public UmkaTypeKind ElementKind { get; init; }

    /// <summary>Gets the Umka element type name for array-like Umka types, or <see langword="null" /> when unavailable.</summary>
    public string? ElementTypeName
    {
        get => _elementTypeName;
        init => _elementTypeName = ValidateOptionalTypeName(value, nameof(value));
    }

    /// <summary>Gets the native Umka element storage size in bytes, or <c>0</c> when unavailable.</summary>
    public int ElementNativeSize
    {
        get => _elementNativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _elementNativeSize = value;
        }
    }

    /// <summary>Gets a value indicating whether array-like element values contain Umka-managed references.</summary>
    public bool ElementHasReferences { get; init; }

    /// <summary>Gets the broad managed classification of the inner element type for nested dynamic arrays.</summary>
    public UmkaTypeKind NestedElementKind { get; init; }

    /// <summary>Gets the Umka inner element type name for nested dynamic arrays, or <see langword="null" /> when unavailable.</summary>
    public string? NestedElementTypeName
    {
        get => _nestedElementTypeName;
        init => _nestedElementTypeName = ValidateOptionalTypeName(value, nameof(value));
    }

    /// <summary>Gets the native Umka inner element storage size in bytes for nested dynamic arrays, or <c>0</c> when unavailable.</summary>
    public int NestedElementNativeSize
    {
        get => _nestedElementNativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _nestedElementNativeSize = value;
        }
    }

    /// <summary>Gets a value indicating whether nested dynamic-array inner element values contain Umka-managed references.</summary>
    public bool NestedElementHasReferences { get; init; }

    /// <summary>Gets the broad managed classification of the key type for Umka map types.</summary>
    public UmkaTypeKind MapKeyKind { get; init; }

    /// <summary>Gets the Umka key type name for Umka map types, or <see langword="null" /> when unavailable.</summary>
    public string? MapKeyTypeName
    {
        get => _mapKeyTypeName;
        init => _mapKeyTypeName = ValidateOptionalTypeName(value, nameof(value));
    }

    /// <summary>Gets the native Umka map key storage size in bytes, or <c>0</c> when unavailable.</summary>
    public int MapKeyNativeSize
    {
        get => _mapKeyNativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _mapKeyNativeSize = value;
        }
    }

    /// <summary>Gets a value indicating whether Umka map key values contain Umka-managed references.</summary>
    public bool MapKeyHasReferences { get; init; }

    /// <summary>Gets the broad managed classification of the value type for Umka map types.</summary>
    public UmkaTypeKind MapValueKind { get; init; }

    /// <summary>Gets the Umka value type name for Umka map types, or <see langword="null" /> when unavailable.</summary>
    public string? MapValueTypeName
    {
        get => _mapValueTypeName;
        init => _mapValueTypeName = ValidateOptionalTypeName(value, nameof(value));
    }

    /// <summary>Gets the native Umka map value storage size in bytes, or <c>0</c> when unavailable.</summary>
    public int MapValueNativeSize
    {
        get => _mapValueNativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _mapValueNativeSize = value;
        }
    }

    /// <summary>Gets a value indicating whether Umka map values contain Umka-managed references.</summary>
    public bool MapValueHasReferences { get; init; }

    /// <summary>Gets the broad managed classification of the dynamic-array element type for map values.</summary>
    public UmkaTypeKind MapValueElementKind { get; init; }

    /// <summary>Gets the Umka dynamic-array element type name for map values, or <see langword="null" /> when unavailable.</summary>
    public string? MapValueElementTypeName
    {
        get => _mapValueElementTypeName;
        init => _mapValueElementTypeName = ValidateOptionalTypeName(value, nameof(value));
    }

    /// <summary>Gets the native dynamic-array element storage size in bytes for map values, or <c>0</c> when unavailable.</summary>
    public int MapValueElementNativeSize
    {
        get => _mapValueElementNativeSize;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _mapValueElementNativeSize = value;
        }
    }

    /// <summary>Gets a value indicating whether dynamic-array element values inside Umka map values contain Umka-managed references.</summary>
    public bool MapValueElementHasReferences { get; init; }

    /// <summary>Gets a value indicating whether this dynamic array type is Umka's representation of a variadic parameter list.</summary>
    public bool IsVariadicParameterList { get; init; }

    /// <summary>Gets a value indicating whether the Umka type contains Umka-managed references.</summary>
    public bool HasReferences { get; init; }

    /// <summary>Gets a value indicating whether the Umka type is an enum stored as its underlying integer kind.</summary>
    public bool IsEnum { get; init; }

    /// <summary>Gets the named enum constants declared on this Umka enum type.</summary>
    public IReadOnlyList<UmkaEnumMemberInfo> EnumMembers
    {
        get => _enumMembers;
        init => _enumMembers = SnapshotEnumMembers(value);
    }

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
        Kind is (UmkaTypeKind.Interface
            or UmkaTypeKind.Closure
            or UmkaTypeKind.Fiber
            or UmkaTypeKind.Function)
            || Kind == UmkaTypeKind.DynamicArray
                && (ElementNativeSize <= 0 || ElementHasReferences && !CanReadAsStringArray() && !HasReadableNestedDynamicArrayMetadata && !HasNestedStringArrayMetadata)
            || Kind == UmkaTypeKind.Map && (MapKeyNativeSize <= 0 || MapValueNativeSize <= 0 || HasUnsupportedMapReferences);

    /// <summary>Returns whether the type can be represented as a dynamic <see cref="UmkaValue" />.</summary>
    public bool CanReadAsValue() =>
        Kind is UmkaTypeKind.Void
            or UmkaTypeKind.SignedInteger
            or UmkaTypeKind.UnsignedInteger
            or UmkaTypeKind.Real
            or UmkaTypeKind.Boolean
            or UmkaTypeKind.Character
            or UmkaTypeKind.String
            or UmkaTypeKind.Pointer
            or UmkaTypeKind.WeakPointer;

    /// <summary>Returns whether the type can be read as a supported scalar, string, pointer, enum, or dynamic value.</summary>
    public bool CanReadAsScalar<T>() => CanReadAsScalarType(typeof(T));

    /// <summary>Returns whether the type can be read as an opaque Umka weak pointer handle.</summary>
    public bool CanReadAsWeakPointer() => Kind == UmkaTypeKind.WeakPointer;

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

    /// <summary>Returns whether the dynamic array type can be copied into a managed array with the given element type.</summary>
    public bool CanReadAsDynamicArray<TElement>() where TElement : struct
    {
        if (Kind != UmkaTypeKind.DynamicArray
            || ElementHasReferences
            || ElementNativeSize <= 0
            || !TryGetMarshalSize<TElement>(out var elementSize))
        {
            return false;
        }

        return elementSize == ElementNativeSize;
    }

    /// <summary>Returns whether the dynamic array type can be copied into a managed string array.</summary>
    public bool CanReadAsStringArray() =>
        Kind == UmkaTypeKind.DynamicArray
        && ElementKind == UmkaTypeKind.String
        && ElementNativeSize == IntPtr.Size;

    /// <summary>Returns whether the nested dynamic array type can be copied into a managed jagged array with the given inner element type.</summary>
    public bool CanReadAsNestedDynamicArray<TElement>() where TElement : struct
    {
        if (!HasReadableNestedDynamicArrayMetadata
            || !TryGetMarshalSize<TElement>(out var elementSize))
        {
            return false;
        }

        return elementSize == NestedElementNativeSize;
    }

    /// <summary>Returns whether the nested dynamic array type can be copied into a managed jagged string array.</summary>
    public bool CanReadAsNestedStringArray() => HasNestedStringArrayMetadata;

    /// <summary>Returns whether the map type can be copied into a managed dictionary with the given key and value types.</summary>
    public bool CanReadAsMap<TKey, TValue>()
        where TKey : struct
        where TValue : struct
    {
        if (Kind != UmkaTypeKind.Map
            || MapKeyHasReferences
            || MapValueHasReferences
            || MapKeyNativeSize <= 0
            || MapValueNativeSize <= 0
            || !TryGetMarshalSize<TKey>(out var keySize)
            || !TryGetMarshalSize<TValue>(out var valueSize))
        {
            return false;
        }

        return keySize == MapKeyNativeSize && valueSize == MapValueNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with string keys and fixed-layout values.</summary>
    public bool CanReadAsStringKeyMap<TValue>() where TValue : struct
    {
        if (Kind != UmkaTypeKind.Map
            || !HasStringMapKey
            || MapValueHasReferences
            || MapValueNativeSize <= 0
            || !TryGetMarshalSize<TValue>(out var valueSize))
        {
            return false;
        }

        return valueSize == MapValueNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with fixed-layout keys and string values.</summary>
    public bool CanReadAsStringValueMap<TKey>() where TKey : struct
    {
        if (Kind != UmkaTypeKind.Map
            || MapKeyHasReferences
            || !HasStringMapValue
            || MapKeyNativeSize <= 0
            || !TryGetMarshalSize<TKey>(out var keySize))
        {
            return false;
        }

        return keySize == MapKeyNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with string keys and string values.</summary>
    public bool CanReadAsStringMap() =>
        Kind == UmkaTypeKind.Map
        && HasStringMapKey
        && HasStringMapValue;

    /// <summary>Returns whether the map type can be copied into a managed dictionary with fixed-layout keys and dynamic-array values.</summary>
    public bool CanReadAsDynamicArrayValueMap<TKey, TElement>()
        where TKey : struct
        where TElement : struct
    {
        if (Kind != UmkaTypeKind.Map
            || MapKeyHasReferences
            || !HasReadableDynamicArrayMapValueMetadata
            || MapKeyNativeSize <= 0
            || !TryGetMarshalSize<TKey>(out var keySize)
            || !TryGetMarshalSize<TElement>(out var elementSize))
        {
            return false;
        }

        return keySize == MapKeyNativeSize && elementSize == MapValueElementNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with string keys and dynamic-array values.</summary>
    public bool CanReadAsStringKeyDynamicArrayValueMap<TElement>() where TElement : struct
    {
        if (Kind != UmkaTypeKind.Map
            || !HasStringMapKey
            || !HasReadableDynamicArrayMapValueMetadata
            || !TryGetMarshalSize<TElement>(out var elementSize))
        {
            return false;
        }

        return elementSize == MapValueElementNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with fixed-layout keys and string-array values.</summary>
    public bool CanReadAsStringArrayValueMap<TKey>() where TKey : struct
    {
        if (Kind != UmkaTypeKind.Map
            || MapKeyHasReferences
            || !HasStringArrayMapValue
            || MapKeyNativeSize <= 0
            || !TryGetMarshalSize<TKey>(out var keySize))
        {
            return false;
        }

        return keySize == MapKeyNativeSize;
    }

    /// <summary>Returns whether the map type can be copied into a managed dictionary with string keys and string-array values.</summary>
    public bool CanReadAsStringKeyStringArrayValueMap() =>
        Kind == UmkaTypeKind.Map
        && HasStringMapKey
        && HasStringArrayMapValue;

    private bool HasStringMapKey =>
        MapKeyKind == UmkaTypeKind.String
        && MapKeyNativeSize == IntPtr.Size;

    private bool HasStringMapValue =>
        MapValueKind == UmkaTypeKind.String
        && MapValueNativeSize == IntPtr.Size;

    private bool HasReadableNestedDynamicArrayMetadata =>
        Kind == UmkaTypeKind.DynamicArray
        && ElementKind == UmkaTypeKind.DynamicArray
        && NestedElementNativeSize > 0
        && !NestedElementHasReferences;

    private bool HasNestedStringArrayMetadata =>
        Kind == UmkaTypeKind.DynamicArray
        && ElementKind == UmkaTypeKind.DynamicArray
        && NestedElementKind == UmkaTypeKind.String
        && NestedElementNativeSize == IntPtr.Size;

    private bool HasReadableDynamicArrayMapValueMetadata =>
        MapValueKind == UmkaTypeKind.DynamicArray
        && MapValueNativeSize == IntPtr.Size * 3
        && MapValueElementNativeSize > 0
        && !MapValueElementHasReferences;

    private bool HasStringArrayMapValue =>
        MapValueKind == UmkaTypeKind.DynamicArray
        && MapValueNativeSize == IntPtr.Size * 3
        && MapValueElementKind == UmkaTypeKind.String
        && MapValueElementNativeSize == IntPtr.Size;

    private bool HasUnsupportedMapReferences =>
        MapKeyHasReferences && !HasStringMapKey
        || MapValueHasReferences && !HasStringMapValue && !HasReadableDynamicArrayMapValueMetadata && !HasStringArrayMapValue;

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
        if (ElementKind != UmkaTypeKind.Unknown)
            details.Add($"ElementKind={ElementKind}");
        if (!string.IsNullOrWhiteSpace(ElementTypeName))
            details.Add($"ElementTypeName={ElementTypeName}");
        if (ElementNativeSize > 0)
            details.Add($"ElementNativeSize={ElementNativeSize}");
        if (ElementHasReferences)
            details.Add("ElementHasReferences=True");
        if (NestedElementKind != UmkaTypeKind.Unknown)
            details.Add($"NestedElementKind={NestedElementKind}");
        if (!string.IsNullOrWhiteSpace(NestedElementTypeName))
            details.Add($"NestedElementTypeName={NestedElementTypeName}");
        if (NestedElementNativeSize > 0)
            details.Add($"NestedElementNativeSize={NestedElementNativeSize}");
        if (NestedElementHasReferences)
            details.Add("NestedElementHasReferences=True");
        if (MapKeyKind != UmkaTypeKind.Unknown)
            details.Add($"MapKeyKind={MapKeyKind}");
        if (!string.IsNullOrWhiteSpace(MapKeyTypeName))
            details.Add($"MapKeyTypeName={MapKeyTypeName}");
        if (MapKeyNativeSize > 0)
            details.Add($"MapKeyNativeSize={MapKeyNativeSize}");
        if (MapKeyHasReferences)
            details.Add("MapKeyHasReferences=True");
        if (MapValueKind != UmkaTypeKind.Unknown)
            details.Add($"MapValueKind={MapValueKind}");
        if (!string.IsNullOrWhiteSpace(MapValueTypeName))
            details.Add($"MapValueTypeName={MapValueTypeName}");
        if (MapValueNativeSize > 0)
            details.Add($"MapValueNativeSize={MapValueNativeSize}");
        if (MapValueHasReferences)
            details.Add("MapValueHasReferences=True");
        if (MapValueElementKind != UmkaTypeKind.Unknown)
            details.Add($"MapValueElementKind={MapValueElementKind}");
        if (!string.IsNullOrWhiteSpace(MapValueElementTypeName))
            details.Add($"MapValueElementTypeName={MapValueElementTypeName}");
        if (MapValueElementNativeSize > 0)
            details.Add($"MapValueElementNativeSize={MapValueElementNativeSize}");
        if (MapValueElementHasReferences)
            details.Add("MapValueElementHasReferences=True");
        if (IsVariadicParameterList)
            details.Add("IsVariadicParameterList=True");
        if (HasReferences)
            details.Add("HasReferences=True");
        if (IsEnum)
        {
            details.Add("IsEnum=True");
            details.Add($"EnumMembers={EnumMembers.Count}");
        }

        return $"UmkaTypeInfo({string.Join(", ", details)})";
    }

    private static string ValidateTypeName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, parameterName);
        return value;
    }

    private static string? ValidateOptionalTypeName(string? value, string parameterName)
    {
        if (value is null)
            return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, parameterName);
        return value;
    }

    private static ReadOnlyCollection<UmkaEnumMemberInfo> SnapshotEnumMembers(IEnumerable<UmkaEnumMemberInfo>? values)
    {
        if (values is null)
            return EmptyEnumMembers;

        var items = values.ToArray();
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i] is null)
                throw new ArgumentException("Enum member metadata cannot contain null entries.", nameof(values));
        }

        return items.Length == 0 ? EmptyEnumMembers : Array.AsReadOnly(items);
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
        bool hasReferences = false,
        bool isEnum = false,
        IReadOnlyList<UmkaEnumMemberInfo>? enumMembers = null,
        NativeUmkaTypeKind elementKind = NativeUmkaTypeKind.None,
        string? elementTypeName = null,
        int elementNativeSize = 0,
        bool elementHasReferences = false,
        NativeUmkaTypeKind nestedElementKind = NativeUmkaTypeKind.None,
        string? nestedElementTypeName = null,
        int nestedElementNativeSize = 0,
        bool nestedElementHasReferences = false,
        NativeUmkaTypeKind mapKeyKind = NativeUmkaTypeKind.None,
        string? mapKeyTypeName = null,
        int mapKeyNativeSize = 0,
        bool mapKeyHasReferences = false,
        NativeUmkaTypeKind mapValueKind = NativeUmkaTypeKind.None,
        string? mapValueTypeName = null,
        int mapValueNativeSize = 0,
        bool mapValueHasReferences = false,
        NativeUmkaTypeKind mapValueElementKind = NativeUmkaTypeKind.None,
        string? mapValueElementTypeName = null,
        int mapValueElementNativeSize = 0,
        bool mapValueElementHasReferences = false,
        bool isVariadicParameterList = false) =>
        new(MapKind(kind), string.IsNullOrWhiteSpace(typeName) ? "unknown" : typeName)
        {
            NativeSize = Math.Max(0, nativeSize),
            ItemCount = Math.Max(0, itemCount),
            HasReferences = hasReferences,
            IsEnum = isEnum,
            EnumMembers = enumMembers ?? Array.Empty<UmkaEnumMemberInfo>(),
            ElementKind = MapKind(elementKind),
            ElementTypeName = string.IsNullOrWhiteSpace(elementTypeName) ? null : elementTypeName,
            ElementNativeSize = Math.Max(0, elementNativeSize),
            ElementHasReferences = elementHasReferences,
            NestedElementKind = MapKind(nestedElementKind),
            NestedElementTypeName = string.IsNullOrWhiteSpace(nestedElementTypeName) ? null : nestedElementTypeName,
            NestedElementNativeSize = Math.Max(0, nestedElementNativeSize),
            NestedElementHasReferences = nestedElementHasReferences,
            MapKeyKind = MapKind(mapKeyKind),
            MapKeyTypeName = string.IsNullOrWhiteSpace(mapKeyTypeName) ? null : mapKeyTypeName,
            MapKeyNativeSize = Math.Max(0, mapKeyNativeSize),
            MapKeyHasReferences = mapKeyHasReferences,
            MapValueKind = MapKind(mapValueKind),
            MapValueTypeName = string.IsNullOrWhiteSpace(mapValueTypeName) ? null : mapValueTypeName,
            MapValueNativeSize = Math.Max(0, mapValueNativeSize),
            MapValueHasReferences = mapValueHasReferences,
            MapValueElementKind = MapKind(mapValueElementKind),
            MapValueElementTypeName = string.IsNullOrWhiteSpace(mapValueElementTypeName) ? null : mapValueElementTypeName,
            MapValueElementNativeSize = Math.Max(0, mapValueElementNativeSize),
            MapValueElementHasReferences = mapValueElementHasReferences,
            IsVariadicParameterList = isVariadicParameterList,
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
