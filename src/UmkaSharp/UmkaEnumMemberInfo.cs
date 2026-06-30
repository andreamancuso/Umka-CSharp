namespace UmkaSharp;

/// <summary>Describes one named constant declared on an Umka enum type.</summary>
public sealed record UmkaEnumMemberInfo(string Name, long SignedValue, ulong UnsignedValue)
{
    private readonly string _name = ValidateName(Name, nameof(Name));

    /// <summary>Gets the Umka enum member name.</summary>
    public string Name
    {
        get => _name;
        init => _name = ValidateName(value, nameof(value));
    }

    /// <summary>Gets the enum member value interpreted as signed storage.</summary>
    public long SignedValue { get; init; } = SignedValue;

    /// <summary>Gets the enum member value interpreted as unsigned storage.</summary>
    public ulong UnsignedValue { get; init; } = UnsignedValue;

    /// <summary>Deconstructs the enum member metadata into its native fields.</summary>
    public void Deconstruct(out string Name, out long SignedValue, out ulong UnsignedValue)
    {
        Name = this.Name;
        SignedValue = this.SignedValue;
        UnsignedValue = this.UnsignedValue;
    }

    private static string ValidateName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, parameterName);
        return value;
    }
}
