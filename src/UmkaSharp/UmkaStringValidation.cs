namespace UmkaSharp;

internal static class UmkaStringValidation
{
    public static void ThrowIfContainsNullCharacter(string? value, string parameterName)
    {
        if (value is not null && value.Contains('\0'))
            throw new ArgumentException("Embedded null characters are not supported across the Umka native string boundary.", parameterName);
    }
}
