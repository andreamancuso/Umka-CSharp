using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Represents a managed callback registered as an Umka external function.</summary>
public sealed class UmkaCallback
{
    /// <summary>Managed function signature used for callbacks invoked from Umka.</summary>
    public delegate UmkaValue CallbackFunc(UmkaCallFrame frame);

    private readonly CallbackFunc _callback;
    private readonly GCHandle _delegateHandle;
    private bool _disposed;
    private IntPtr _nativeSlot;

    internal UmkaCallback(CallbackFunc callback)
    {
        _callback = callback;
        NativeDelegate = Invoke;
        _delegateHandle = GCHandle.Alloc(NativeDelegate);
    }

    internal NativeMethods.ManagedCallback NativeDelegate { get; }

    /// <summary>Gets the last managed exception thrown by this callback, if any.</summary>
    public Exception? LastException { get; private set; }

    internal void SetNativeSlot(IntPtr slot)
    {
        _nativeSlot = slot;
    }

    private int Invoke(IntPtr state, IntPtr parameters, IntPtr result)
    {
        _ = state;
        try
        {
            LastException = null;
            var returnValue = _callback(new UmkaCallFrame(parameters, result));
            new UmkaCallFrame(parameters, result).SetResult(returnValue);
            return 0;
        }
        catch (Exception ex)
        {
            LastException = ex;
            return 1;
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

        if (_delegateHandle.IsAllocated)
            _delegateHandle.Free();
    }
}
