using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Describes the last Umka compile-time or runtime error.</summary>
public sealed record UmkaError(string? FileName, string? FunctionName, int Line, int Position, int Code, string? Message)
{
    private readonly string? _fileName = ValidateOptionalNativeString(FileName, nameof(FileName));
    private readonly string? _functionName = ValidateOptionalNativeString(FunctionName, nameof(FunctionName));
    private readonly int _line = ValidateNonNegative(Line, nameof(Line));
    private readonly int _position = ValidateNonNegative(Position, nameof(Position));
    private readonly string? _message = ValidateOptionalNativeString(Message, nameof(Message));

    /// <summary>Gets the source file associated with the error, when Umka reported one.</summary>
    public string? FileName
    {
        get => _fileName;
        init => _fileName = ValidateOptionalNativeString(value, nameof(value));
    }

    /// <summary>Gets the Umka function associated with the error, when Umka reported one.</summary>
    public string? FunctionName
    {
        get => _functionName;
        init => _functionName = ValidateOptionalNativeString(value, nameof(value));
    }

    /// <summary>Gets the one-based source line associated with the error, or zero when unavailable.</summary>
    public int Line
    {
        get => _line;
        init => _line = ValidateNonNegative(value, nameof(value));
    }

    /// <summary>Gets the source position associated with the error, or zero when unavailable.</summary>
    public int Position
    {
        get => _position;
        init => _position = ValidateNonNegative(value, nameof(value));
    }

    /// <summary>Gets Umka's native error code.</summary>
    public int Code { get; init; } = Code;

    /// <summary>Gets Umka's native diagnostic message.</summary>
    public string? Message
    {
        get => _message;
        init => _message = ValidateOptionalNativeString(value, nameof(value));
    }

    /// <summary>Deconstructs the error into its native diagnostic fields.</summary>
    public void Deconstruct(
        out string? FileName,
        out string? FunctionName,
        out int Line,
        out int Position,
        out int Code,
        out string? Message)
    {
        FileName = this.FileName;
        FunctionName = this.FunctionName;
        Line = this.Line;
        Position = this.Position;
        Code = this.Code;
        Message = this.Message;
    }

    internal static UmkaError FromNative(IntPtr runtime)
    {
        return new UmkaError(
            NativeMethods.ErrorFileName(runtime).ToManagedString(),
            NativeMethods.ErrorFunctionName(runtime).ToManagedString(),
            NativeMethods.ErrorLine(runtime),
            NativeMethods.ErrorPosition(runtime),
            NativeMethods.ErrorCode(runtime),
            NativeMethods.ErrorMessage(runtime).ToManagedString());
    }

    internal static UmkaError FromNativeReport(IntPtr report)
    {
        if (report == IntPtr.Zero)
            return new UmkaError(null, null, 0, 0, 0, null);

        var native = Marshal.PtrToStructure<NativeReport>(report);
        return new UmkaError(
            native.FileName.ToManagedString(),
            native.FunctionName.ToManagedString(),
            native.Line,
            native.Position,
            native.Code,
            native.Message.ToManagedString());
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeReport
    {
        public readonly IntPtr FileName;
        public readonly IntPtr FunctionName;
        public readonly int Line;
        public readonly int Position;
        public readonly int Code;
        public readonly IntPtr Message;
    }

    private static string? ValidateOptionalNativeString(string? value, string parameterName)
    {
        UmkaStringValidation.ThrowIfContainsNullCharacter(value, parameterName);
        return value;
    }

    private static int ValidateNonNegative(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value, parameterName);
        return value;
    }
}

internal static class NativeStringExtensions
{
    public static string? ToManagedString(this IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
}
