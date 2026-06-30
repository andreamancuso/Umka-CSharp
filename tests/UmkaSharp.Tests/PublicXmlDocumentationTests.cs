using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class PublicXmlDocumentationTests
{
    private const BindingFlags PublicDeclared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    [Fact]
    public void Public_api_members_have_packaged_xml_summaries()
    {
        var members = ReadXmlDocumentation();
        var missing = GetExpectedXmlMemberNames(typeof(UmkaRuntime).Assembly)
            .Where(memberName => !HasUsableSummary(members, memberName))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void Public_xml_documentation_does_not_emit_unresolved_inheritdoc()
    {
        var unresolved = ReadXmlDocumentation()
            .Where(item => item.Value.Descendants("inheritdoc").Any())
            .Select(item => item.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(unresolved);
    }

    private static Dictionary<string, XElement> ReadXmlDocumentation()
    {
        var xmlPath = Path.ChangeExtension(typeof(UmkaRuntime).Assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath), $"Expected XML documentation at '{xmlPath}'.");

        var document = XDocument.Load(xmlPath);
        return document
            .Descendants("member")
            .Select(member => new
            {
                Name = (string?)member.Attribute("name"),
                Member = member,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToDictionary(item => item.Name!, item => item.Member, StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetExpectedXmlMemberNames(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            yield return $"T:{FormatXmlTypeName(type)}";

            if (IsDelegate(type))
                continue;

            foreach (var constructor in type.GetConstructors(PublicDeclared)
                .Where(constructor => !IsCompilerGenerated(constructor))
                .OrderBy(constructor => constructor.ToString(), StringComparer.Ordinal))
            {
                yield return FormatXmlMethodName(constructor);
            }

            foreach (var property in type.GetProperties(PublicDeclared)
                .Where(HasPublicAccessor)
                .Where(property => !IsCompilerGenerated(property))
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                yield return $"P:{FormatXmlTypeName(property.DeclaringType!)}.{property.Name}";
            }

            foreach (var field in type.GetFields(PublicDeclared)
                .Where(field => !field.IsSpecialName)
                .Where(field => !IsCompilerGenerated(field))
                .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                yield return $"F:{FormatXmlTypeName(field.DeclaringType!)}.{field.Name}";
            }

            foreach (var method in type.GetMethods(PublicDeclared)
                .Where(method => !method.IsSpecialName)
                .Where(method => method.Name is not "Equals" and not "GetHashCode" and not "ToString")
                .Where(method => !method.Name.Contains('<', StringComparison.Ordinal))
                .Where(method => !IsCompilerGenerated(method))
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.ToString(), StringComparer.Ordinal))
            {
                yield return FormatXmlMethodName(method);
            }
        }
    }

    private static bool HasUsableSummary(Dictionary<string, XElement> members, string memberName)
    {
        if (!members.TryGetValue(memberName, out var member))
            return false;

        var summary = member.Element("summary");
        return summary is not null && !string.IsNullOrWhiteSpace(summary.Value);
    }

    private static bool HasPublicAccessor(PropertyInfo property) =>
        property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true;

    private static bool IsDelegate(Type type) =>
        typeof(MulticastDelegate).IsAssignableFrom(type.BaseType);

    private static bool IsCompilerGenerated(MemberInfo member) =>
        member.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    private static string FormatXmlMethodName(MethodBase method)
    {
        var declaringType = FormatXmlTypeName(method.DeclaringType!);
        var methodName = method.IsConstructor ? "#ctor" : method.Name;
        if (method is MethodInfo { IsGenericMethodDefinition: true } methodInfo)
            methodName += $"``{methodInfo.GetGenericArguments().Length}";

        var parameters = method.GetParameters();
        return parameters.Length == 0
            ? $"M:{declaringType}.{methodName}"
            : $"M:{declaringType}.{methodName}({string.Join(",", parameters.Select(parameter => FormatXmlParameterType(parameter.ParameterType)))})";
    }

    private static string FormatXmlParameterType(Type type)
    {
        if (type.IsByRef)
            return $"{FormatXmlParameterType(type.GetElementType()!)}@";
        if (type.IsArray)
            return $"{FormatXmlParameterType(type.GetElementType()!)}[]";
        if (type.IsGenericParameter)
            return type.DeclaringMethod is null
                ? $"`{type.GenericParameterPosition}"
                : $"``{type.GenericParameterPosition}";
        if (type.IsGenericType)
        {
            var definitionName = FormatXmlTypeName(type.GetGenericTypeDefinition());
            var backtickIndex = definitionName.IndexOf('`', StringComparison.Ordinal);
            if (backtickIndex >= 0)
                definitionName = definitionName[..backtickIndex];

            return $"{definitionName}{{{string.Join(",", type.GetGenericArguments().Select(FormatXmlParameterType))}}}";
        }

        return FormatXmlTypeName(type);
    }

    private static string FormatXmlTypeName(Type type) =>
        (type.FullName ?? type.Name).Replace('+', '.');
}
