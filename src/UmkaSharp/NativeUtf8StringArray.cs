namespace UmkaSharp;

using System.Runtime.InteropServices;

internal sealed class NativeUtf8StringArray : IDisposable
{
    private readonly IntPtr[] _strings;
    private bool _disposed;

    public NativeUtf8StringArray(IReadOnlyList<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        _strings = new IntPtr[values.Count];
        try
        {
            for (var i = 0; i < values.Count; i++)
                _strings[i] = values[i] is null ? IntPtr.Zero : Marshal.StringToCoTaskMemUTF8(values[i]);

            if (_strings.Length > 0)
            {
                Pointer = Marshal.AllocHGlobal(checked(IntPtr.Size * _strings.Length));
                Marshal.Copy(_strings, 0, Pointer, _strings.Length);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IntPtr Pointer { get; private set; }

    public int Length => _strings.Length;

    public void Dispose()
    {
        if (_disposed)
            return;

        for (var i = 0; i < _strings.Length; i++)
        {
            if (_strings[i] != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_strings[i]);
                _strings[i] = IntPtr.Zero;
            }
        }

        if (Pointer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
