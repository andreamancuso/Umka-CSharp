namespace UmkaSharp;

/// <summary>Exception thrown when Umka reports a compile-time or runtime error.</summary>
public sealed class UmkaException : Exception
{
    /// <summary>Gets the Umka error details associated with the exception.</summary>
    public UmkaError Error { get; }

    /// <summary>Initializes an exception from an Umka error report.</summary>
    public UmkaException(UmkaError error)
        : this(error, innerException: null)
    {
    }

    /// <summary>Initializes an exception from an Umka error report and a managed inner exception.</summary>
    public UmkaException(UmkaError error, Exception? innerException)
        : base(FormatMessage(error ?? throw new ArgumentNullException(nameof(error))), innerException)
    {
        Error = error;
    }

    /// <summary>Initializes an exception from a managed error message.</summary>
    public UmkaException(string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Error = new UmkaError(null, null, 0, 0, 1, message);
    }

    private static string FormatMessage(UmkaError error)
    {
        var message = string.IsNullOrWhiteSpace(error.Message) ? "Umka error" : error.Message!;
        var hasSourceLocation = !string.IsNullOrWhiteSpace(error.FileName);
        var hasFunctionName = !string.IsNullOrWhiteSpace(error.FunctionName);
        var hasSourceCoordinates = error.Line > 0 || error.Position > 0;

        if (hasSourceLocation && hasSourceCoordinates && hasFunctionName)
            return $"{message} ({error.FileName}:{error.Line}:{error.Position}; function {error.FunctionName})";

        if (hasSourceLocation && hasSourceCoordinates)
            return $"{message} ({error.FileName}:{error.Line}:{error.Position})";

        if (hasSourceLocation && hasFunctionName)
            return $"{message} ({error.FileName}; function {error.FunctionName})";

        if (hasSourceLocation)
            return $"{message} ({error.FileName})";

        if (hasFunctionName)
            return $"{message} (function {error.FunctionName})";

        return message;
    }
}
