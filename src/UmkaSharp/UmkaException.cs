namespace UmkaSharp;

/// <summary>Exception thrown when Umka reports a compile-time or runtime error.</summary>
public sealed class UmkaException : Exception
{
    /// <summary>Gets the Umka error details associated with the exception.</summary>
    public UmkaError Error { get; }

    /// <summary>Initializes an exception from an Umka error report.</summary>
    public UmkaException(UmkaError error)
        : base(FormatMessage(error))
    {
        Error = error;
    }

    /// <summary>Initializes an exception from a managed error message.</summary>
    public UmkaException(string message)
        : base(message)
    {
        Error = new UmkaError(null, null, 0, 0, 1, message);
    }

    private static string FormatMessage(UmkaError error)
    {
        var message = string.IsNullOrWhiteSpace(error.Message) ? "Umka error" : error.Message!;
        if (!string.IsNullOrWhiteSpace(error.FileName))
            return $"{message} ({error.FileName}:{error.Line}:{error.Position})";
        return message;
    }
}
