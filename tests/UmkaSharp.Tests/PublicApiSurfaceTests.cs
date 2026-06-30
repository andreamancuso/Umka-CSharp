using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class PublicApiSurfaceTests
{
    [Fact]
    public void Public_api_surface_matches_curated_baseline()
    {
        var actual = DescribePublicApi(typeof(UmkaRuntime).Assembly);

        Assert.Equal(ExpectedPublicApi.ReplaceLineEndings("\n").TrimEnd(), actual);
    }

    private static string DescribePublicApi(Assembly assembly)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var lines = new List<string>();

        foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (IsDelegate(type))
            {
                var invoke = type.GetMethod("Invoke")!;
                lines.Add($"delegate {FormatTypeName(type)}({FormatParameters(invoke.GetParameters())}) -> {FormatReturnType(invoke)}");
                continue;
            }

            var kind = type.IsEnum
                ? "enum"
                : type.IsValueType
                    ? "struct"
                    : type.IsClass
                        ? "class"
                        : type.IsInterface
                            ? "interface"
                            : "type";

            lines.Add($"{kind} {FormatTypeName(type)}");
            foreach (var constructor in type.GetConstructors(Flags).OrderBy(constructor => constructor.ToString(), StringComparer.Ordinal))
                lines.Add($"  .ctor({FormatParameters(constructor.GetParameters())})");

            foreach (var property in type.GetProperties(Flags).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                var accessors = new List<string>();
                if (property.GetMethod?.IsPublic == true)
                    accessors.Add("get");
                if (property.SetMethod?.IsPublic == true)
                    accessors.Add(IsInitOnly(property) ? "init" : "set");

                lines.Add($"  property {FormatPropertyType(property)} {property.Name} {{ {string.Join(", ", accessors)} }}");
            }

            foreach (var field in type.GetFields(Flags).Where(field => !field.IsSpecialName).OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                var prefix = field.IsLiteral && !field.IsInitOnly ? "const" : "field";
                var value = field.IsLiteral ? $" = {FormatConstantValue(field.GetRawConstantValue())}" : "";
                lines.Add($"  {prefix} {FormatTypeName(field.FieldType)} {field.Name}{value}");
            }

            foreach (var method in type.GetMethods(Flags)
                .Where(method => !method.IsSpecialName)
                .Where(method => method.Name is not "Equals" and not "GetHashCode" and not "ToString")
                .Where(method => !method.Name.Contains('<', StringComparison.Ordinal))
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.ToString(), StringComparer.Ordinal))
            {
                var genericArguments = method.IsGenericMethodDefinition
                    ? $"<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>"
                    : "";

                lines.Add($"  method {FormatReturnType(method)} {method.Name}{genericArguments}({FormatParameters(method.GetParameters())}){FormatGenericConstraints(method.GetGenericArguments())}");
            }
        }

        return string.Join('\n', lines);
    }

    private static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(", ", parameters.Select(FormatParameter));

    private static string FormatParameter(ParameterInfo parameter)
    {
        var attributes = FormatParameterAttributes(parameter);
        var kind = parameter.IsOut
            ? "out "
            : parameter.ParameterType.IsByRef
                ? "ref "
                : parameter.GetCustomAttribute<ParamArrayAttribute>() is not null
                    ? "params "
                    : "";
        var defaultValue = parameter.HasDefaultValue
            ? $" = {FormatConstantValue(parameter.DefaultValue)}"
            : "";

        return $"{attributes}{kind}{FormatParameterType(parameter)} {parameter.Name}{defaultValue}";
    }

    private static string FormatParameterAttributes(ParameterInfo parameter)
    {
        var notNullWhen = parameter.GetCustomAttribute<NotNullWhenAttribute>();
        var maybeNull = parameter.GetCustomAttribute<MaybeNullAttribute>();
        var maybeNullWhen = parameter.GetCustomAttribute<MaybeNullWhenAttribute>();
        var attributes = new List<string>();
        if (notNullWhen is not null)
            attributes.Add($"NotNullWhen({FormatConstantValue(notNullWhen.ReturnValue)})");
        if (maybeNull is not null)
            attributes.Add("MaybeNull");
        if (maybeNullWhen is not null)
            attributes.Add($"MaybeNullWhen({FormatConstantValue(maybeNullWhen.ReturnValue)})");

        return attributes.Count == 0 ? "" : $"[{string.Join(", ", attributes)}] ";
    }

    private static string FormatConstantValue(object? value) =>
        value switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            string text => $"\"{EscapeString(text)}\"",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

    private static string EscapeString(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string FormatTypeName(Type type) =>
        FormatTypeNameCore(type, null);

    private static string FormatParameterType(ParameterInfo parameter)
    {
        var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var nullability = new NullabilityInfoContext().Create(parameter);
        return FormatTypeNameCore(
            type,
            nullability,
            includeGenericParameterNullability: parameter.ParameterType.IsByRef);
    }

    private static string FormatPropertyType(PropertyInfo property)
    {
        var nullability = new NullabilityInfoContext().Create(property);
        return FormatTypeNameCore(property.PropertyType, nullability);
    }

    private static string FormatReturnType(MethodInfo method)
    {
        var nullability = new NullabilityInfoContext().Create(method.ReturnParameter);
        return FormatTypeNameCore(method.ReturnType, nullability);
    }

    private static string FormatTypeNameCore(
        Type type,
        NullabilityInfo? nullability,
        bool includeGenericParameterNullability = false)
    {
        if (type.IsGenericParameter)
            return includeGenericParameterNullability ? $"{type.Name}{NullableSuffix(type, nullability)}" : type.Name;
        if (type.IsByRef)
            return $"{FormatTypeNameCore(type.GetElementType()!, nullability?.ElementType, includeGenericParameterNullability)}&";
        if (type.IsArray)
            return $"{FormatTypeNameCore(type.GetElementType()!, nullability?.ElementType, includeGenericParameterNullability)}[]{NullableSuffix(type, nullability)}";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return $"{FormatTypeNameCore(type.GetGenericArguments()[0], nullability?.GenericTypeArguments.FirstOrDefault(), includeGenericParameterNullability)}?";
        if (type.IsGenericType)
        {
            var builder = new StringBuilder(type.FullName ?? type.Name);
            builder.Replace('+', '.');
            var backtickIndex = builder.ToString().IndexOf('`', StringComparison.Ordinal);
            if (backtickIndex >= 0)
                builder.Remove(backtickIndex, builder.Length - backtickIndex);

            var arguments = type.GetGenericArguments();
            var nullabilityArguments = nullability?.GenericTypeArguments ?? Array.Empty<NullabilityInfo>();
            return $"{builder}<{string.Join(", ", arguments.Select((argument, index) => FormatTypeNameCore(argument, index < nullabilityArguments.Length ? nullabilityArguments[index] : null, includeGenericParameterNullability)))}>{NullableSuffix(type, nullability)}";
        }

        return $"{(type.FullName ?? type.Name).Replace('+', '.')}{NullableSuffix(type, nullability)}";
    }

    private static bool IsDelegate(Type type) =>
        typeof(MulticastDelegate).IsAssignableFrom(type.BaseType);

    private static bool IsInitOnly(PropertyInfo property) =>
        property.SetMethod?.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(IsExternalInit)) == true;

    private static string NullableSuffix(Type type, NullabilityInfo? nullability) =>
        !type.IsValueType && nullability?.ReadState == NullabilityState.Nullable ? "?" : "";

    private static string FormatGenericConstraints(Type[] genericArguments)
    {
        var constraints = genericArguments
            .Select(FormatGenericConstraint)
            .Where(constraint => constraint.Length > 0);

        return string.Concat(constraints);
    }

    private static string FormatGenericConstraint(Type genericArgument)
    {
        var attributes = genericArgument.GenericParameterAttributes;
        var constraints = new List<string>();

        if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            constraints.Add("struct");
        else if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            constraints.Add("class");

        constraints.AddRange(
            genericArgument
                .GetGenericParameterConstraints()
                .Where(constraint => constraint != typeof(ValueType) ||
                    (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
                .Select(FormatTypeName));

        if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 &&
            (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0)
            constraints.Add("new()");

        return constraints.Count == 0
            ? ""
            : $" where {genericArgument.Name} : {string.Join(", ", constraints)}";
    }

    private const string ExpectedPublicApi = """
struct UmkaSharp.UmkaCallFrame
  property System.Int32 ParameterCount { get }
  property System.Collections.Generic.IReadOnlyList<UmkaSharp.UmkaTypeInfo> ParameterTypes { get }
  property UmkaSharp.UmkaTypeInfo ResultType { get }
  method System.Boolean CanReadArgumentAsArray<TElement>(System.Int32 index, System.Int32 length) where TElement : struct
  method System.Boolean CanReadArgumentAsDynamicArray<TElement>(System.Int32 index) where TElement : struct
  method System.Boolean CanReadArgumentAsDynamicArrayValueMap<TKey, TElement>(System.Int32 index) where TKey : struct where TElement : struct
  method System.Boolean CanReadArgumentAsMap<TKey, TValue>(System.Int32 index) where TKey : struct where TValue : struct
  method System.Boolean CanReadArgumentAsNestedDynamicArray<TElement>(System.Int32 index) where TElement : struct
  method System.Boolean CanReadArgumentAsNestedStringArray(System.Int32 index)
  method System.Boolean CanReadArgumentAsScalar<T>(System.Int32 index)
  method System.Boolean CanReadArgumentAsStringArray(System.Int32 index)
  method System.Boolean CanReadArgumentAsStringArrayValueMap<TKey>(System.Int32 index) where TKey : struct
  method System.Boolean CanReadArgumentAsStringKeyDynamicArrayValueMap<TElement>(System.Int32 index) where TElement : struct
  method System.Boolean CanReadArgumentAsStringKeyMap<TValue>(System.Int32 index) where TValue : struct
  method System.Boolean CanReadArgumentAsStringKeyStringArrayValueMap(System.Int32 index)
  method System.Boolean CanReadArgumentAsStringMap(System.Int32 index)
  method System.Boolean CanReadArgumentAsStringValueMap<TKey>(System.Int32 index) where TKey : struct
  method System.Boolean CanReadArgumentAsStruct<T>(System.Int32 index) where T : struct
  method System.Boolean CanReadArgumentAsValue(System.Int32 index)
  method System.Boolean CanReadArgumentAsWeakPointer(System.Int32 index)
  method System.Boolean CanReturn(UmkaSharp.UmkaValue value)
  method TElement[] GetArray<TElement>(System.Int32 index, System.Int32 length) where TElement : struct
  method System.Boolean GetBoolean(System.Int32 index)
  method System.Byte GetByte(System.Int32 index)
  method System.Char GetChar(System.Int32 index)
  method System.Double GetDouble(System.Int32 index)
  method TElement[] GetDynamicArray<TElement>(System.Int32 index) where TElement : struct
  method Dictionary<TKey, TElement[]> GetDynamicArrayValueMap<TKey, TElement>(System.Int32 index) where TKey : struct where TElement : struct
  method TEnum GetEnum<TEnum>(System.Int32 index) where TEnum : struct, System.Enum
  method T GetHostObject<T>(System.Int32 index)
  method System.Int16 GetInt16(System.Int32 index)
  method System.Int32 GetInt32(System.Int32 index)
  method System.Int64 GetInt64(System.Int32 index)
  method Dictionary<TKey, TValue> GetMap<TKey, TValue>(System.Int32 index) where TKey : struct where TValue : struct
  method TElement[][] GetNestedDynamicArray<TElement>(System.Int32 index) where TElement : struct
  method System.String?[][] GetNestedStringArray(System.Int32 index)
  method System.IntPtr GetPointer(System.Int32 index)
  method System.SByte GetSByte(System.Int32 index)
  method T GetScalar<T>(System.Int32 index)
  method System.Single GetSingle(System.Int32 index)
  method System.String? GetString(System.Int32 index)
  method System.String?[] GetStringArray(System.Int32 index)
  method Dictionary<TKey, System.String[]> GetStringArrayValueMap<TKey>(System.Int32 index) where TKey : struct
  method Dictionary<System.String, TElement[]> GetStringKeyDynamicArrayValueMap<TElement>(System.Int32 index) where TElement : struct
  method Dictionary<System.String, TValue> GetStringKeyMap<TValue>(System.Int32 index) where TValue : struct
  method System.Collections.Generic.Dictionary<System.String, System.String?[]> GetStringKeyStringArrayValueMap(System.Int32 index)
  method System.Collections.Generic.Dictionary<System.String, System.String?> GetStringMap(System.Int32 index)
  method Dictionary<TKey, System.String> GetStringValueMap<TKey>(System.Int32 index) where TKey : struct
  method T GetStruct<T>(System.Int32 index) where T : struct
  method System.UInt16 GetUInt16(System.Int32 index)
  method System.UInt32 GetUInt32(System.Int32 index)
  method System.UInt64 GetUInt64(System.Int32 index)
  method UmkaSharp.UmkaValue GetValue(System.Int32 index)
  method System.UInt64 GetWeakPointer(System.Int32 index)
  method System.Boolean TryGetArray<TElement>(System.Int32 index, System.Int32 length, [NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryGetDynamicArray<TElement>(System.Int32 index, [NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryGetDynamicArrayValueMap<TKey, TElement>(System.Int32 index, [NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value) where TKey : struct where TElement : struct
  method System.Boolean TryGetEnum<TEnum>(System.Int32 index, out TEnum value) where TEnum : struct, System.Enum
  method System.Boolean TryGetHostObject<T>(System.Int32 index, [NotNullWhen(true)] out T? target)
  method System.Boolean TryGetMap<TKey, TValue>(System.Int32 index, [NotNullWhen(true)] out Dictionary<TKey, TValue>? value) where TKey : struct where TValue : struct
  method System.Boolean TryGetNestedDynamicArray<TElement>(System.Int32 index, [NotNullWhen(true)] out TElement[][]? value) where TElement : struct
  method System.Boolean TryGetNestedStringArray(System.Int32 index, [NotNullWhen(true)] out System.String?[][]? value)
  method System.Boolean TryGetScalar<T>(System.Int32 index, [MaybeNull] out T? value)
  method System.Boolean TryGetStringArray(System.Int32 index, [NotNullWhen(true)] out System.String?[]? value)
  method System.Boolean TryGetStringArrayValueMap<TKey>(System.Int32 index, [NotNullWhen(true)] out Dictionary<TKey, System.String[]>? value) where TKey : struct
  method System.Boolean TryGetStringKeyDynamicArrayValueMap<TElement>(System.Int32 index, [NotNullWhen(true)] out Dictionary<System.String, TElement[]>? value) where TElement : struct
  method System.Boolean TryGetStringKeyMap<TValue>(System.Int32 index, [NotNullWhen(true)] out Dictionary<System.String, TValue>? value) where TValue : struct
  method System.Boolean TryGetStringKeyStringArrayValueMap(System.Int32 index, [NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?[]>? value)
  method System.Boolean TryGetStringMap(System.Int32 index, [NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?>? value)
  method System.Boolean TryGetStringValueMap<TKey>(System.Int32 index, [NotNullWhen(true)] out Dictionary<TKey, System.String>? value) where TKey : struct
  method System.Boolean TryGetStruct<T>(System.Int32 index, out T value) where T : struct
  method System.Boolean TryGetValue(System.Int32 index, out UmkaSharp.UmkaValue value)
  method System.Boolean TryGetWeakPointer(System.Int32 index, out System.UInt64 value)
class UmkaSharp.UmkaCallback
  property System.Boolean IsDisposed { get }
  property System.Exception? LastException { get }
  property System.String Name { get }
delegate UmkaSharp.UmkaCallback.CallbackFunc(UmkaSharp.UmkaCallFrame frame) -> UmkaSharp.UmkaValue
class UmkaSharp.UmkaEnumMemberInfo
  .ctor(System.String Name, System.Int64 SignedValue, System.UInt64 UnsignedValue)
  property System.String Name { get, init }
  property System.Int64 SignedValue { get, init }
  property System.UInt64 UnsignedValue { get, init }
  method System.Void Deconstruct(out System.String Name, out System.Int64 SignedValue, out System.UInt64 UnsignedValue)
class UmkaSharp.UmkaError
  .ctor(System.String? FileName, System.String? FunctionName, System.Int32 Line, System.Int32 Position, System.Int32 Code, System.String? Message)
  property System.Int32 Code { get, init }
  property System.String? FileName { get, init }
  property System.String? FunctionName { get, init }
  property System.Int32 Line { get, init }
  property System.String? Message { get, init }
  property System.Int32 Position { get, init }
  method System.Void Deconstruct(out System.String? FileName, out System.String? FunctionName, out System.Int32 Line, out System.Int32 Position, out System.Int32 Code, out System.String? Message)
class UmkaSharp.UmkaException
  .ctor(System.String message)
  .ctor(UmkaSharp.UmkaError error)
  .ctor(UmkaSharp.UmkaError error, System.Exception? innerException)
  property UmkaSharp.UmkaError Error { get }
class UmkaSharp.UmkaFunction
  property System.Int32 DefaultParameterCount { get }
  property System.String? ModuleName { get }
  property System.String Name { get }
  property System.Int32 ParameterCount { get }
  property System.Collections.Generic.IReadOnlyList<UmkaSharp.UmkaTypeInfo> ParameterTypes { get }
  property System.String QualifiedName { get }
  property System.Int32 RequiredParameterCount { get }
  property UmkaSharp.UmkaTypeInfo ResultType { get }
  method TElement[] CallArray<TElement>(System.Int32 length, System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TElement : struct
  method TElement[] CallArray<TElement>(System.Int32 length, params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.Boolean CallBoolean(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Boolean CallBoolean(params UmkaSharp.UmkaValue[] arguments)
  method System.Byte CallByte(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Byte CallByte(params UmkaSharp.UmkaValue[] arguments)
  method System.Char CallChar(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Char CallChar(params UmkaSharp.UmkaValue[] arguments)
  method System.Double CallDouble(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Double CallDouble(params UmkaSharp.UmkaValue[] arguments)
  method TElement[] CallDynamicArray<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TElement : struct
  method TElement[] CallDynamicArray<TElement>(params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method Dictionary<TKey, TElement[]> CallDynamicArrayValueMap<TKey, TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TKey : struct where TElement : struct
  method Dictionary<TKey, TElement[]> CallDynamicArrayValueMap<TKey, TElement>(params UmkaSharp.UmkaValue[] arguments) where TKey : struct where TElement : struct
  method TEnum CallEnum<TEnum>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TEnum : struct, System.Enum
  method TEnum CallEnum<TEnum>(params UmkaSharp.UmkaValue[] arguments) where TEnum : struct, System.Enum
  method T CallHostObject<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method T CallHostObject<T>(params UmkaSharp.UmkaValue[] arguments)
  method System.Int16 CallInt16(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Int16 CallInt16(params UmkaSharp.UmkaValue[] arguments)
  method System.Int32 CallInt32(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Int32 CallInt32(params UmkaSharp.UmkaValue[] arguments)
  method System.Int64 CallInt64(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Int64 CallInt64(params UmkaSharp.UmkaValue[] arguments)
  method Dictionary<TKey, TValue> CallMap<TKey, TValue>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TKey : struct where TValue : struct
  method Dictionary<TKey, TValue> CallMap<TKey, TValue>(params UmkaSharp.UmkaValue[] arguments) where TKey : struct where TValue : struct
  method TElement[][] CallNestedDynamicArray<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TElement : struct
  method TElement[][] CallNestedDynamicArray<TElement>(params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.String?[][] CallNestedStringArray(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.String?[][] CallNestedStringArray(params UmkaSharp.UmkaValue[] arguments)
  method System.IntPtr CallPointer(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.IntPtr CallPointer(params UmkaSharp.UmkaValue[] arguments)
  method System.SByte CallSByte(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.SByte CallSByte(params UmkaSharp.UmkaValue[] arguments)
  method T CallScalar<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method T CallScalar<T>(params UmkaSharp.UmkaValue[] arguments)
  method System.Single CallSingle(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Single CallSingle(params UmkaSharp.UmkaValue[] arguments)
  method System.String? CallString(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.String? CallString(params UmkaSharp.UmkaValue[] arguments)
  method System.String?[] CallStringArray(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.String?[] CallStringArray(params UmkaSharp.UmkaValue[] arguments)
  method Dictionary<TKey, System.String[]> CallStringArrayValueMap<TKey>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TKey : struct
  method Dictionary<TKey, System.String[]> CallStringArrayValueMap<TKey>(params UmkaSharp.UmkaValue[] arguments) where TKey : struct
  method Dictionary<System.String, TElement[]> CallStringKeyDynamicArrayValueMap<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TElement : struct
  method Dictionary<System.String, TElement[]> CallStringKeyDynamicArrayValueMap<TElement>(params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method Dictionary<System.String, TValue> CallStringKeyMap<TValue>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TValue : struct
  method Dictionary<System.String, TValue> CallStringKeyMap<TValue>(params UmkaSharp.UmkaValue[] arguments) where TValue : struct
  method System.Collections.Generic.Dictionary<System.String, System.String?[]> CallStringKeyStringArrayValueMap(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Collections.Generic.Dictionary<System.String, System.String?[]> CallStringKeyStringArrayValueMap(params UmkaSharp.UmkaValue[] arguments)
  method System.Collections.Generic.Dictionary<System.String, System.String?> CallStringMap(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Collections.Generic.Dictionary<System.String, System.String?> CallStringMap(params UmkaSharp.UmkaValue[] arguments)
  method Dictionary<TKey, System.String> CallStringValueMap<TKey>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where TKey : struct
  method Dictionary<TKey, System.String> CallStringValueMap<TKey>(params UmkaSharp.UmkaValue[] arguments) where TKey : struct
  method T CallStruct<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments) where T : struct
  method T CallStruct<T>(params UmkaSharp.UmkaValue[] arguments) where T : struct
  method System.UInt16 CallUInt16(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.UInt16 CallUInt16(params UmkaSharp.UmkaValue[] arguments)
  method System.UInt32 CallUInt32(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.UInt32 CallUInt32(params UmkaSharp.UmkaValue[] arguments)
  method System.UInt64 CallUInt64(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.UInt64 CallUInt64(params UmkaSharp.UmkaValue[] arguments)
  method UmkaSharp.UmkaValue CallValue(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method UmkaSharp.UmkaValue CallValue(params UmkaSharp.UmkaValue[] arguments)
  method System.Void CallVoid(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Void CallVoid(params UmkaSharp.UmkaValue[] arguments)
  method System.UInt64 CallWeakPointer(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.UInt64 CallWeakPointer(params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean CanCallWith(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Boolean CanCallWith(params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean CanReadResultAsArray<TElement>(System.Int32 length) where TElement : struct
  method System.Boolean CanReadResultAsDynamicArray<TElement>() where TElement : struct
  method System.Boolean CanReadResultAsDynamicArrayValueMap<TKey, TElement>() where TKey : struct where TElement : struct
  method System.Boolean CanReadResultAsMap<TKey, TValue>() where TKey : struct where TValue : struct
  method System.Boolean CanReadResultAsNestedDynamicArray<TElement>() where TElement : struct
  method System.Boolean CanReadResultAsNestedStringArray()
  method System.Boolean CanReadResultAsScalar<T>()
  method System.Boolean CanReadResultAsStringArray()
  method System.Boolean CanReadResultAsStringArrayValueMap<TKey>() where TKey : struct
  method System.Boolean CanReadResultAsStringKeyDynamicArrayValueMap<TElement>() where TElement : struct
  method System.Boolean CanReadResultAsStringKeyMap<TValue>() where TValue : struct
  method System.Boolean CanReadResultAsStringKeyStringArrayValueMap()
  method System.Boolean CanReadResultAsStringMap()
  method System.Boolean CanReadResultAsStringValueMap<TKey>() where TKey : struct
  method System.Boolean CanReadResultAsStruct<T>() where T : struct
  method System.Boolean CanReadResultAsValue()
  method System.Boolean CanReadResultAsWeakPointer()
  method System.Boolean TryCallArray<TElement>(System.Int32 length, System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryCallArray<TElement>(System.Int32 length, [NotNullWhen(true)] out TElement[]? value, params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.Boolean TryCallDynamicArray<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryCallDynamicArray<TElement>([NotNullWhen(true)] out TElement[]? value, params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.Boolean TryCallDynamicArrayValueMap<TKey, TElement>([NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value, params UmkaSharp.UmkaValue[] arguments) where TKey : struct where TElement : struct
  method System.Boolean TryCallDynamicArrayValueMap<TKey, TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<TKey, TElement[]>? value) where TKey : struct where TElement : struct
  method System.Boolean TryCallEnum<TEnum>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, out TEnum value) where TEnum : struct, System.Enum
  method System.Boolean TryCallEnum<TEnum>(out TEnum value, params UmkaSharp.UmkaValue[] arguments) where TEnum : struct, System.Enum
  method System.Boolean TryCallHostObject<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out T? target)
  method System.Boolean TryCallHostObject<T>([NotNullWhen(true)] out T? target, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallMap<TKey, TValue>([NotNullWhen(true)] out Dictionary<TKey, TValue>? value, params UmkaSharp.UmkaValue[] arguments) where TKey : struct where TValue : struct
  method System.Boolean TryCallMap<TKey, TValue>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<TKey, TValue>? value) where TKey : struct where TValue : struct
  method System.Boolean TryCallNestedDynamicArray<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out TElement[][]? value) where TElement : struct
  method System.Boolean TryCallNestedDynamicArray<TElement>([NotNullWhen(true)] out TElement[][]? value, params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.Boolean TryCallNestedStringArray(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out System.String?[][]? value)
  method System.Boolean TryCallNestedStringArray([NotNullWhen(true)] out System.String?[][]? value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallScalar<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [MaybeNull] out T? value)
  method System.Boolean TryCallScalar<T>([MaybeNull] out T? value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallStringArray(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out System.String?[]? value)
  method System.Boolean TryCallStringArray([NotNullWhen(true)] out System.String?[]? value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallStringArrayValueMap<TKey>([NotNullWhen(true)] out Dictionary<TKey, System.String[]>? value, params UmkaSharp.UmkaValue[] arguments) where TKey : struct
  method System.Boolean TryCallStringArrayValueMap<TKey>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<TKey, System.String[]>? value) where TKey : struct
  method System.Boolean TryCallStringKeyDynamicArrayValueMap<TElement>([NotNullWhen(true)] out Dictionary<System.String, TElement[]>? value, params UmkaSharp.UmkaValue[] arguments) where TElement : struct
  method System.Boolean TryCallStringKeyDynamicArrayValueMap<TElement>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<System.String, TElement[]>? value) where TElement : struct
  method System.Boolean TryCallStringKeyMap<TValue>([NotNullWhen(true)] out Dictionary<System.String, TValue>? value, params UmkaSharp.UmkaValue[] arguments) where TValue : struct
  method System.Boolean TryCallStringKeyMap<TValue>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<System.String, TValue>? value) where TValue : struct
  method System.Boolean TryCallStringKeyStringArrayValueMap([NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?[]>? value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallStringKeyStringArrayValueMap(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?[]>? value)
  method System.Boolean TryCallStringMap([NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?>? value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallStringMap(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out System.Collections.Generic.Dictionary<System.String, System.String?>? value)
  method System.Boolean TryCallStringValueMap<TKey>([NotNullWhen(true)] out Dictionary<TKey, System.String>? value, params UmkaSharp.UmkaValue[] arguments) where TKey : struct
  method System.Boolean TryCallStringValueMap<TKey>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, [NotNullWhen(true)] out Dictionary<TKey, System.String>? value) where TKey : struct
  method System.Boolean TryCallStruct<T>(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, out T value) where T : struct
  method System.Boolean TryCallStruct<T>(out T value, params UmkaSharp.UmkaValue[] arguments) where T : struct
  method System.Boolean TryCallValue(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, out UmkaSharp.UmkaValue value)
  method System.Boolean TryCallValue(out UmkaSharp.UmkaValue value, params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallVoid(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments)
  method System.Boolean TryCallVoid(params UmkaSharp.UmkaValue[] arguments)
  method System.Boolean TryCallWeakPointer(System.ReadOnlySpan<UmkaSharp.UmkaValue> arguments, out System.UInt64 value)
  method System.Boolean TryCallWeakPointer(out System.UInt64 value, params UmkaSharp.UmkaValue[] arguments)
class UmkaSharp.UmkaHostHandle
  property System.IntPtr Address { get }
  property System.Boolean IsDisposed { get }
  property System.Object Target { get }
  method System.Void Dispose()
  method T GetTarget<T>()
  method UmkaSharp.UmkaValue ToValue()
  method System.Boolean TryGetTarget<T>([NotNullWhen(true)] out T? target)
class UmkaSharp.UmkaRuntime
  property System.Collections.Generic.IReadOnlyList<System.String> Arguments { get }
  property System.Boolean FileSystemEnabled { get }
  property System.Boolean ImplementationLibrariesEnabled { get }
  property System.Boolean IsAlive { get }
  property System.Boolean IsDisposed { get }
  property System.Exception? LastCallbackException { get }
  property System.Collections.Generic.IReadOnlyList<System.String> RegisteredCallbackNames { get }
  property System.Collections.Generic.IReadOnlyList<System.String> RegisteredModuleNames { get }
  property System.String SourceFileName { get }
  property System.Int32 StackSize { get }
  property UmkaSharp.UmkaRuntimeState State { get }
  const System.Int32 DefaultStackSize = 1048576
  method System.Void AddModule(System.String fileName, System.String source)
  method System.Void AddModuleFromFile(System.String fileName)
  method System.Void AddModuleFromFile(System.String moduleName, System.String fileName)
  method System.Void Compile()
  method UmkaSharp.UmkaRuntime CompileFile(System.String fileName, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method UmkaSharp.UmkaRuntime CompileSource(System.String source, System.String fileName = "main.um", System.Int32 stackSize = 1048576, System.Boolean fileSystemEnabled = false, System.Boolean implementationLibrariesEnabled = false, System.Collections.Generic.IReadOnlyList<System.String>? arguments = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method UmkaSharp.UmkaRuntime CompileSource(System.String source, System.String fileName, UmkaSharp.UmkaRuntimeOptions? options, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method UmkaSharp.UmkaRuntime CompileSource(System.String source, UmkaSharp.UmkaRuntimeOptions? options, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method UmkaSharp.UmkaHostHandle CreateHostHandle(System.Object target)
  method System.Void Dispose()
  method UmkaSharp.UmkaRuntime FromFile(System.String fileName, System.Int32 stackSize = 1048576, System.Boolean fileSystemEnabled = false, System.Boolean implementationLibrariesEnabled = false, System.Collections.Generic.IReadOnlyList<System.String>? arguments = null)
  method UmkaSharp.UmkaRuntime FromFile(System.String fileName, UmkaSharp.UmkaRuntimeOptions? options)
  method UmkaSharp.UmkaRuntime FromSource(System.String source, System.String fileName = "main.um", System.Int32 stackSize = 1048576, System.Boolean fileSystemEnabled = false, System.Boolean implementationLibrariesEnabled = false, System.Collections.Generic.IReadOnlyList<System.String>? arguments = null)
  method UmkaSharp.UmkaRuntime FromSource(System.String source, System.String fileName, UmkaSharp.UmkaRuntimeOptions? options)
  method UmkaSharp.UmkaRuntime FromSource(System.String source, UmkaSharp.UmkaRuntimeOptions? options)
  method UmkaSharp.UmkaCallback GetCallback(System.String name)
  method UmkaSharp.UmkaFunction GetFunction(System.String functionName, System.String? moduleName = null)
  method T GetHostObject<T>(System.IntPtr handleAddress)
  method UmkaSharp.UmkaError GetLastError()
  method UmkaSharp.UmkaCallback Register(System.String name, UmkaSharp.UmkaCallback.CallbackFunc callback)
  method UmkaSharp.UmkaCallback RegisterVoid(System.String name, System.Action<UmkaSharp.UmkaCallFrame> callback)
  method System.Void Run()
  method System.Void RunSource(System.String source, System.String fileName = "main.um", System.Int32 stackSize = 1048576, System.Boolean fileSystemEnabled = false, System.Boolean implementationLibrariesEnabled = false, System.Collections.Generic.IReadOnlyList<System.String>? arguments = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Void RunSource(System.String source, System.String fileName, UmkaSharp.UmkaRuntimeOptions? options, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Void RunSource(System.String source, UmkaSharp.UmkaRuntimeOptions? options, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Boolean TryCompile([NotNullWhen(false)] out UmkaSharp.UmkaError? error)
  method System.Boolean TryCompileFile(System.String fileName, [NotNullWhen(true)] out UmkaSharp.UmkaRuntime? runtime, [NotNullWhen(false)] out UmkaSharp.UmkaError? error, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Boolean TryCompileSource(System.String source, System.String fileName, [NotNullWhen(true)] out UmkaSharp.UmkaRuntime? runtime, [NotNullWhen(false)] out UmkaSharp.UmkaError? error, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Boolean TryCompileSource(System.String source, [NotNullWhen(true)] out UmkaSharp.UmkaRuntime? runtime, [NotNullWhen(false)] out UmkaSharp.UmkaError? error, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Boolean TryGetCallback(System.String name, [NotNullWhen(true)] out UmkaSharp.UmkaCallback? callback)
  method System.Boolean TryGetFunction(System.String functionName, System.String moduleName, [NotNullWhen(true)] out UmkaSharp.UmkaFunction? function)
  method System.Boolean TryGetFunction(System.String functionName, [NotNullWhen(true)] out UmkaSharp.UmkaFunction? function)
  method System.Boolean TryGetFunction(System.String functionName, [NotNullWhen(true)] out UmkaSharp.UmkaFunction? function, System.String? moduleName = null)
  method System.Boolean TryGetHostObject<T>(System.IntPtr handleAddress, [NotNullWhen(true)] out T? target)
  method System.Boolean TryGetLastError([NotNullWhen(true)] out UmkaSharp.UmkaError? error)
  method System.Boolean TryRun([NotNullWhen(false)] out UmkaSharp.UmkaException? exception)
  method System.Boolean TryRunSource(System.String source, System.String fileName, [NotNullWhen(false)] out UmkaSharp.UmkaException? exception, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
  method System.Boolean TryRunSource(System.String source, [NotNullWhen(false)] out UmkaSharp.UmkaException? exception, UmkaSharp.UmkaRuntimeOptions? options = null, System.Action<UmkaSharp.UmkaRuntime>? configure = null)
class UmkaSharp.UmkaRuntimeOptions
  .ctor()
  property System.Collections.Generic.IReadOnlyList<System.String>? Arguments { get, init }
  property System.Boolean FileSystemEnabled { get, init }
  property System.Boolean ImplementationLibrariesEnabled { get, init }
  property System.Int32 StackSize { get, init }
  property System.Action<UmkaSharp.UmkaError>? WarningHandler { get, init }
enum UmkaSharp.UmkaRuntimeState
  const UmkaSharp.UmkaRuntimeState CompileAttempted = 1
  const UmkaSharp.UmkaRuntimeState Compiled = 2
  const UmkaSharp.UmkaRuntimeState Created = 0
  const UmkaSharp.UmkaRuntimeState Disposed = 4
  const UmkaSharp.UmkaRuntimeState Terminated = 3
class UmkaSharp.UmkaTypeInfo
  .ctor(UmkaSharp.UmkaTypeKind Kind, System.String TypeName)
  property System.Boolean ElementHasReferences { get, init }
  property UmkaSharp.UmkaTypeKind ElementKind { get, init }
  property System.Int32 ElementNativeSize { get, init }
  property System.String? ElementTypeName { get, init }
  property System.Collections.Generic.IReadOnlyList<UmkaSharp.UmkaEnumMemberInfo> EnumMembers { get, init }
  property System.Boolean HasReferences { get, init }
  property System.Boolean IsAggregate { get }
  property System.Boolean IsDeferred { get }
  property System.Boolean IsEnum { get, init }
  property System.Boolean IsScalar { get }
  property System.Boolean IsVariadicParameterList { get, init }
  property System.Int32 ItemCount { get, init }
  property UmkaSharp.UmkaTypeKind Kind { get, init }
  property System.Boolean MapKeyHasReferences { get, init }
  property UmkaSharp.UmkaTypeKind MapKeyKind { get, init }
  property System.Int32 MapKeyNativeSize { get, init }
  property System.String? MapKeyTypeName { get, init }
  property System.Boolean MapValueElementHasReferences { get, init }
  property UmkaSharp.UmkaTypeKind MapValueElementKind { get, init }
  property System.Int32 MapValueElementNativeSize { get, init }
  property System.String? MapValueElementTypeName { get, init }
  property System.Boolean MapValueHasReferences { get, init }
  property UmkaSharp.UmkaTypeKind MapValueKind { get, init }
  property System.Int32 MapValueNativeSize { get, init }
  property System.String? MapValueTypeName { get, init }
  property System.Int32 NativeSize { get, init }
  property System.Boolean NestedElementHasReferences { get, init }
  property UmkaSharp.UmkaTypeKind NestedElementKind { get, init }
  property System.Int32 NestedElementNativeSize { get, init }
  property System.String? NestedElementTypeName { get, init }
  property System.String TypeName { get, init }
  method System.Boolean CanReadAsArray<TElement>(System.Int32 length) where TElement : struct
  method System.Boolean CanReadAsDynamicArray<TElement>() where TElement : struct
  method System.Boolean CanReadAsDynamicArrayValueMap<TKey, TElement>() where TKey : struct where TElement : struct
  method System.Boolean CanReadAsFixedLayout<T>() where T : struct
  method System.Boolean CanReadAsMap<TKey, TValue>() where TKey : struct where TValue : struct
  method System.Boolean CanReadAsNestedDynamicArray<TElement>() where TElement : struct
  method System.Boolean CanReadAsNestedStringArray()
  method System.Boolean CanReadAsScalar<T>()
  method System.Boolean CanReadAsStringArray()
  method System.Boolean CanReadAsStringArrayValueMap<TKey>() where TKey : struct
  method System.Boolean CanReadAsStringKeyDynamicArrayValueMap<TElement>() where TElement : struct
  method System.Boolean CanReadAsStringKeyMap<TValue>() where TValue : struct
  method System.Boolean CanReadAsStringKeyStringArrayValueMap()
  method System.Boolean CanReadAsStringMap()
  method System.Boolean CanReadAsStringValueMap<TKey>() where TKey : struct
  method System.Boolean CanReadAsStruct<T>() where T : struct
  method System.Boolean CanReadAsValue()
  method System.Boolean CanReadAsWeakPointer()
  method System.Void Deconstruct(out UmkaSharp.UmkaTypeKind Kind, out System.String TypeName)
enum UmkaSharp.UmkaTypeKind
  const UmkaSharp.UmkaTypeKind Boolean = 5
  const UmkaSharp.UmkaTypeKind Character = 6
  const UmkaSharp.UmkaTypeKind Closure = 16
  const UmkaSharp.UmkaTypeKind DynamicArray = 11
  const UmkaSharp.UmkaTypeKind Fiber = 17
  const UmkaSharp.UmkaTypeKind Function = 18
  const UmkaSharp.UmkaTypeKind Interface = 15
  const UmkaSharp.UmkaTypeKind Map = 13
  const UmkaSharp.UmkaTypeKind Null = 2
  const UmkaSharp.UmkaTypeKind Pointer = 8
  const UmkaSharp.UmkaTypeKind Real = 7
  const UmkaSharp.UmkaTypeKind SignedInteger = 3
  const UmkaSharp.UmkaTypeKind StaticArray = 10
  const UmkaSharp.UmkaTypeKind String = 12
  const UmkaSharp.UmkaTypeKind Struct = 14
  const UmkaSharp.UmkaTypeKind Unknown = 0
  const UmkaSharp.UmkaTypeKind UnsignedInteger = 4
  const UmkaSharp.UmkaTypeKind Void = 1
  const UmkaSharp.UmkaTypeKind WeakPointer = 9
struct UmkaSharp.UmkaValue
  property UmkaSharp.UmkaValueKind Kind { get }
  property System.Object? Value { get }
  property UmkaSharp.UmkaValue Void { get }
  method System.Boolean AsBoolean()
  method System.Byte AsByte()
  method System.Char AsChar()
  method System.Double AsDouble()
  method TElement[] AsDynamicArray<TElement>() where TElement : struct
  method TEnum AsEnum<TEnum>() where TEnum : struct, System.Enum
  method System.Int16 AsInt16()
  method System.Int32 AsInt32()
  method System.Int64 AsInt64()
  method TElement[][] AsNestedDynamicArray<TElement>() where TElement : struct
  method System.String?[][] AsNestedStringArray()
  method System.IntPtr AsPointer()
  method System.SByte AsSByte()
  method T AsScalar<T>()
  method System.Single AsSingle()
  method TElement[] AsStaticArray<TElement>() where TElement : struct
  method System.String? AsString()
  method System.String?[] AsStringArray()
  method T AsStruct<T>() where T : struct
  method System.UInt16 AsUInt16()
  method System.UInt32 AsUInt32()
  method System.UInt64 AsUInt64()
  method System.UInt64 AsWeakPointer()
  method UmkaSharp.UmkaValue From(System.Boolean value)
  method UmkaSharp.UmkaValue From(System.Byte value)
  method UmkaSharp.UmkaValue From(System.Char value)
  method UmkaSharp.UmkaValue From(System.Double value)
  method UmkaSharp.UmkaValue From(System.Int16 value)
  method UmkaSharp.UmkaValue From(System.Int32 value)
  method UmkaSharp.UmkaValue From(System.Int64 value)
  method UmkaSharp.UmkaValue From(System.SByte value)
  method UmkaSharp.UmkaValue From(System.Single value)
  method UmkaSharp.UmkaValue From(System.String? value)
  method UmkaSharp.UmkaValue From(System.UInt16 value)
  method UmkaSharp.UmkaValue From(System.UInt32 value)
  method UmkaSharp.UmkaValue From(System.UInt64 value)
  method UmkaSharp.UmkaValue FromDynamicArray(System.ReadOnlySpan<System.String?> values)
  method UmkaSharp.UmkaValue FromDynamicArray(System.Span<System.String?> values)
  method UmkaSharp.UmkaValue FromDynamicArray(params System.String?[] values)
  method UmkaSharp.UmkaValue FromDynamicArray<TElement>(ReadOnlySpan<TElement> values) where TElement : struct
  method UmkaSharp.UmkaValue FromDynamicArray<TElement>(Span<TElement> values) where TElement : struct
  method UmkaSharp.UmkaValue FromDynamicArray<TElement>(params TElement[] values) where TElement : struct
  method UmkaSharp.UmkaValue FromEnum<TEnum>(TEnum value) where TEnum : struct, System.Enum
  method UmkaSharp.UmkaValue FromHostHandle(UmkaSharp.UmkaHostHandle handle)
  method UmkaSharp.UmkaValue FromNestedDynamicArray(System.ReadOnlySpan<System.String?[]> values)
  method UmkaSharp.UmkaValue FromNestedDynamicArray(System.Span<System.String?[]> values)
  method UmkaSharp.UmkaValue FromNestedDynamicArray(params System.String?[][] values)
  method UmkaSharp.UmkaValue FromNestedDynamicArray<TElement>(ReadOnlySpan<TElement[]> values) where TElement : struct
  method UmkaSharp.UmkaValue FromNestedDynamicArray<TElement>(Span<TElement[]> values) where TElement : struct
  method UmkaSharp.UmkaValue FromNestedDynamicArray<TElement>(params TElement[][] values) where TElement : struct
  method UmkaSharp.UmkaValue FromPointer(System.IntPtr value)
  method UmkaSharp.UmkaValue FromScalar<T>(T value)
  method UmkaSharp.UmkaValue FromStaticArray<TElement>(ReadOnlySpan<TElement> values) where TElement : struct
  method UmkaSharp.UmkaValue FromStaticArray<TElement>(Span<TElement> values) where TElement : struct
  method UmkaSharp.UmkaValue FromStaticArray<TElement>(params TElement[] values) where TElement : struct
  method UmkaSharp.UmkaValue FromStruct<T>(T value) where T : struct
  method UmkaSharp.UmkaValue FromWeakPointer(System.UInt64 value)
  method System.Boolean TryAsDynamicArray<TElement>([NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryAsEnum<TEnum>(out TEnum value) where TEnum : struct, System.Enum
  method System.Boolean TryAsNestedDynamicArray<TElement>([NotNullWhen(true)] out TElement[][]? value) where TElement : struct
  method System.Boolean TryAsNestedStringArray([NotNullWhen(true)] out System.String?[][]? value)
  method System.Boolean TryAsScalar<T>([MaybeNull] out T? value)
  method System.Boolean TryAsStaticArray<TElement>([NotNullWhen(true)] out TElement[]? value) where TElement : struct
  method System.Boolean TryAsStringArray([NotNullWhen(true)] out System.String?[]? value)
  method System.Boolean TryAsStruct<T>(out T value) where T : struct
  method System.Boolean TryAsWeakPointer(out System.UInt64 value)
  method System.Boolean TryFromDynamicArray(System.ReadOnlySpan<System.String?> values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromDynamicArray(System.Span<System.String?> values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromDynamicArray(System.String?[]? values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromDynamicArray<TElement>(ReadOnlySpan<TElement> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromDynamicArray<TElement>(Span<TElement> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromDynamicArray<TElement>(TElement[]? values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromNestedDynamicArray(System.ReadOnlySpan<System.String?[]> values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromNestedDynamicArray(System.Span<System.String?[]> values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromNestedDynamicArray(System.String?[][]? values, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromNestedDynamicArray<TElement>(ReadOnlySpan<TElement[]> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromNestedDynamicArray<TElement>(Span<TElement[]> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromNestedDynamicArray<TElement>(TElement[][]? values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromScalar<T>(T value, out UmkaSharp.UmkaValue result)
  method System.Boolean TryFromStaticArray<TElement>(ReadOnlySpan<TElement> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromStaticArray<TElement>(Span<TElement> values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromStaticArray<TElement>(TElement[]? values, out UmkaSharp.UmkaValue result) where TElement : struct
  method System.Boolean TryFromStruct<T>(T value, out UmkaSharp.UmkaValue result) where T : struct
enum UmkaSharp.UmkaValueKind
  const UmkaSharp.UmkaValueKind Bool = 4
  const UmkaSharp.UmkaValueKind DynamicArray = 9
  const UmkaSharp.UmkaValueKind Int = 1
  const UmkaSharp.UmkaValueKind Pointer = 6
  const UmkaSharp.UmkaValueKind Real = 3
  const UmkaSharp.UmkaValueKind StaticArray = 7
  const UmkaSharp.UmkaValueKind String = 5
  const UmkaSharp.UmkaValueKind Struct = 8
  const UmkaSharp.UmkaValueKind UInt = 2
  const UmkaSharp.UmkaValueKind Void = 0
  const UmkaSharp.UmkaValueKind WeakPointer = 10
""";
}
