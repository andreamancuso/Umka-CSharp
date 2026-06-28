using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Describes the last Umka compile-time or runtime error.</summary>
public sealed record UmkaError(string? FileName, string? FunctionName, int Line, int Position, int Code, string? Message)
{
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
}

internal static class NativeStringExtensions
{
    public static string? ToManagedString(this IntPtr value) =>
        value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(value);
}
