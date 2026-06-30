namespace UmkaSharp;

using System.Globalization;

internal static class UmkaEnumConversion
{
    public static UmkaValue ToUmkaValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return ToUmkaValue(typeof(TEnum), value);
    }

    public static UmkaValue ToUmkaValue(Type enumType, object value)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => UmkaValue.From(checked((sbyte)Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.Int16 => UmkaValue.From(checked((short)Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.Int32 => UmkaValue.From(checked((int)Convert.ToInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.Int64 => UmkaValue.From(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            TypeCode.Byte => UmkaValue.From(checked((byte)Convert.ToUInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.UInt16 => UmkaValue.From(checked((ushort)Convert.ToUInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.UInt32 => UmkaValue.From(checked((uint)Convert.ToUInt64(value, CultureInfo.InvariantCulture))),
            TypeCode.UInt64 => UmkaValue.From(Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            _ => throw CreateUnsupportedEnumException(enumType)
        };
    }

    public static TEnum ToEnum<TEnum>(long value)
        where TEnum : struct, Enum
    {
        var enumType = typeof(TEnum);
        var rawValue = GetUnderlyingTypeCode<TEnum>() switch
        {
            TypeCode.SByte => checked((sbyte)value),
            TypeCode.Int16 => checked((short)value),
            TypeCode.Int32 => checked((int)value),
            TypeCode.Int64 => value,
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => throw CreateWrongStorageException(enumType, "signed", "unsigned"),
            _ => throw CreateUnsupportedEnumException(enumType)
        };

        return (TEnum)Enum.ToObject(enumType, rawValue);
    }

    public static TEnum ToEnum<TEnum>(ulong value)
        where TEnum : struct, Enum
    {
        var enumType = typeof(TEnum);
        var rawValue = GetUnderlyingTypeCode<TEnum>() switch
        {
            TypeCode.Byte => checked((byte)value),
            TypeCode.UInt16 => checked((ushort)value),
            TypeCode.UInt32 => checked((uint)value),
            TypeCode.UInt64 => value,
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => throw CreateWrongStorageException(enumType, "unsigned", "signed"),
            _ => throw CreateUnsupportedEnumException(enumType)
        };

        return (TEnum)Enum.ToObject(enumType, rawValue);
    }

    public static bool IsUnsigned<TEnum>()
        where TEnum : struct, Enum
    {
        return IsUnsigned(typeof(TEnum));
    }

    public static bool IsUnsigned(Type enumType)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => true,
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => false,
            _ => throw CreateUnsupportedEnumException(enumType)
        };
    }

    private static TypeCode GetUnderlyingTypeCode<TEnum>()
        where TEnum : struct, Enum =>
        Type.GetTypeCode(Enum.GetUnderlyingType(typeof(TEnum)));

    private static InvalidOperationException CreateWrongStorageException(
        Type enumType,
        string valueStorage,
        string expectedStorage) =>
        new($"Enum type {enumType.FullName} has {expectedStorage} underlying storage and cannot be read from an Umka {valueStorage} integer value.");

    private static InvalidOperationException CreateUnsupportedEnumException(Type enumType) =>
        new($"Enum type {enumType.FullName} has an unsupported underlying storage type.");
}
