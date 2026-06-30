namespace UmkaSharp;

/// <summary>Represents a managed callback registered as an Umka external function.</summary>
public sealed class UmkaCallback
{
    /// <summary>Managed function signature used for callbacks invoked from Umka.</summary>
    public delegate UmkaValue CallbackFunc(UmkaCallFrame frame);

    private readonly UmkaRuntime _runtime;
    private readonly CallbackFunc _callback;
    private bool _disposed;
    private IntPtr _nativeSlot;

    internal UmkaCallback(UmkaRuntime runtime, string name, CallbackFunc callback)
    {
        _runtime = runtime;
        Name = name;
        _callback = callback;
        NativeDelegate = Invoke;
    }

    internal NativeMethods.ManagedCallback NativeDelegate { get; }

    /// <summary>Gets the Umka callback name registered on the owning runtime.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the owning runtime has disposed this callback.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets the last managed exception thrown by this callback, if any.</summary>
    public Exception? LastException { get; private set; }

    /// <summary>Returns a diagnostic string that includes the callback name and registration state.</summary>
    public override string ToString() =>
        $"UmkaCallback({Name}, {(_disposed ? "Disposed" : "Registered")})";

    internal void SetNativeSlot(IntPtr slot)
    {
        _nativeSlot = slot;
    }

    private int Invoke(IntPtr state, IntPtr parameters, IntPtr result)
    {
        _ = state;
        var frameId = 0L;
        try
        {
            frameId = _runtime.BeginCallbackFrame();
            LastException = null;
            var frame = new UmkaCallFrame(_runtime, frameId, parameters, result);
            var returnValue = _callback(frame);
            frame.SetResult(returnValue);
            return 0;
        }
        catch (Exception ex)
        {
            LastException = ex;
            _runtime.SetLastCallbackException(ex);
            return 1;
        }
        finally
        {
            if (frameId != 0)
                _runtime.EndCallbackFrame(frameId);
        }
    }

    internal void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_nativeSlot != IntPtr.Zero)
        {
            NativeMethods.FreeCallback(_nativeSlot);
            _nativeSlot = IntPtr.Zero;
        }
    }
}
