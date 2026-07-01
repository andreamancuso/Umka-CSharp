namespace UmkaSharp;

internal static class UmkaMapValueCompatibility
{
    public static bool CanWrite(UmkaTypeInfo targetType, UmkaValue value)
    {
        if (targetType.Kind != UmkaTypeKind.Map || value.Kind != UmkaValueKind.Map)
            return false;

        return CanWriteKey(targetType, value) && CanWriteValue(targetType, value);
    }

    public static void Validate(string targetDescription, UmkaTypeInfo targetType, UmkaValue value, string parameterName)
    {
        if (targetType.Kind != UmkaTypeKind.Map)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka type '{targetType.TypeName}', which is not a map.",
                parameterName);
        }

        ValidateKey(targetDescription, targetType, value, parameterName);
        ValidateValue(targetDescription, targetType, value, parameterName);
    }

    private static bool CanWriteKey(UmkaTypeInfo targetType, UmkaValue value)
    {
        if (targetType.MapKeyKind == UmkaTypeKind.String)
            return value.IsStringKeyMap && targetType.MapKeyNativeSize == IntPtr.Size;

        return !value.IsStringKeyMap
            && !targetType.MapKeyHasReferences
            && targetType.MapKeyNativeSize > 0
            && value.MapKeySize == targetType.MapKeyNativeSize;
    }

    private static bool CanWriteValue(UmkaTypeInfo targetType, UmkaValue value)
    {
        if (targetType.MapValueKind == UmkaTypeKind.String)
            return value.IsStringValueMap && targetType.MapValueNativeSize == IntPtr.Size;

        return !value.IsStringValueMap
            && !targetType.MapValueHasReferences
            && targetType.MapValueNativeSize > 0
            && value.MapValueSize == targetType.MapValueNativeSize;
    }

    private static void ValidateKey(string targetDescription, UmkaTypeInfo targetType, UmkaValue value, string parameterName)
    {
        if (targetType.MapKeyKind == UmkaTypeKind.String)
        {
            if (!value.IsStringKeyMap)
            {
                throw new ArgumentException(
                    $"{targetDescription} expects Umka map type '{targetType.TypeName}' with string keys, but a fixed-layout key map value was provided.",
                    parameterName);
            }

            if (targetType.MapKeyNativeSize != IntPtr.Size)
            {
                throw new ArgumentException(
                    $"{targetDescription} expects Umka map type '{targetType.TypeName}', but its string key size metadata is unavailable.",
                    parameterName);
            }

            return;
        }

        if (value.IsStringKeyMap)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}' with fixed-layout keys, but a string-key map value was provided.",
                parameterName);
        }

        if (targetType.MapKeyHasReferences)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}', whose key type '{targetType.MapKeyTypeName ?? "unknown"}' contains Umka-managed references and cannot be constructed from a managed map value.",
                parameterName);
        }

        if (targetType.MapKeyNativeSize <= 0)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}', but its native key size is unavailable.",
                parameterName);
        }

        if (value.MapKeySize != targetType.MapKeyNativeSize)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}' with native key size {targetType.MapKeyNativeSize} bytes, but map value keys have size {value.MapKeySize} bytes.",
                parameterName);
        }
    }

    private static void ValidateValue(string targetDescription, UmkaTypeInfo targetType, UmkaValue value, string parameterName)
    {
        if (targetType.MapValueKind == UmkaTypeKind.String)
        {
            if (!value.IsStringValueMap)
            {
                throw new ArgumentException(
                    $"{targetDescription} expects Umka map type '{targetType.TypeName}' with string values, but a fixed-layout value map was provided.",
                    parameterName);
            }

            if (targetType.MapValueNativeSize != IntPtr.Size)
            {
                throw new ArgumentException(
                    $"{targetDescription} expects Umka map type '{targetType.TypeName}', but its string value size metadata is unavailable.",
                    parameterName);
            }

            return;
        }

        if (value.IsStringValueMap)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}' with fixed-layout values, but a string-value map was provided.",
                parameterName);
        }

        if (targetType.MapValueHasReferences)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}', whose value type '{targetType.MapValueTypeName ?? "unknown"}' contains Umka-managed references and cannot be constructed from a managed map value.",
                parameterName);
        }

        if (targetType.MapValueNativeSize <= 0)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}', but its native value size is unavailable.",
                parameterName);
        }

        if (value.MapValueSize != targetType.MapValueNativeSize)
        {
            throw new ArgumentException(
                $"{targetDescription} expects Umka map type '{targetType.TypeName}' with native value size {targetType.MapValueNativeSize} bytes, but map value values have size {value.MapValueSize} bytes.",
                parameterName);
        }
    }
}
