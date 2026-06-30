using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Represents a runtime-owned opaque handle to a managed host object.</summary>
public sealed class UmkaHostHandle : IDisposable
{
    private readonly UmkaRuntime _runtime;
    private readonly string _targetTypeName;
    private GCHandle _handle;
    private IntPtr _pointer;
    private bool _disposed;

    internal UmkaHostHandle(UmkaRuntime runtime, object target)
    {
        _runtime = runtime;
        _targetTypeName = target.GetType().FullName ?? target.GetType().Name;
        _handle = GCHandle.Alloc(target);
        _pointer = GCHandle.ToIntPtr(_handle);
    }

    /// <summary>Gets the opaque address value that can be passed through Umka pointer parameters.</summary>
    public IntPtr Address
    {
        get
        {
            ThrowIfDisposed();
            return _pointer;
        }
    }

    /// <summary>Gets the managed target object.</summary>
    public object Target
    {
        get
        {
            ThrowIfDisposed();
            return _handle.Target!;
        }
    }

    /// <summary>Gets a value indicating whether this runtime-owned handle has been disposed.</summary>
    public bool IsDisposed => _disposed;

    internal IntPtr RawPointer => _pointer;

    internal object RawTarget
    {
        get
        {
            ThrowIfDisposed();
            return _handle.Target!;
        }
    }

    /// <summary>Gets the managed target object as the requested type.</summary>
    public T GetTarget<T>()
    {
        var target = Target;
        return target is T typed
            ? typed
            : throw new InvalidCastException(
                $"Host handle target type {target.GetType().FullName} cannot be read as {typeof(T).FullName}.");
    }

    /// <summary>Tries to get the managed target object as the requested type.</summary>
    public bool TryGetTarget<T>([NotNullWhen(true)] out T? target)
    {
        var value = Target;
        if (value is T typed)
        {
            target = typed;
            return true;
        }

        target = default;
        return false;
    }

    /// <summary>Creates an Umka pointer value for this host handle.</summary>
    public UmkaValue ToValue() => UmkaValue.FromPointer(Address);

    /// <summary>Returns a diagnostic string that includes the target type and disposal state.</summary>
    public override string ToString() =>
        $"UmkaHostHandle(Target={_targetTypeName}, {(_disposed ? "Disposed" : "Alive")})";

    /// <summary>Releases the host handle and removes it from the owning runtime.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _runtime.ReleaseHostHandle(this);
        GC.SuppressFinalize(this);
    }

    internal void DisposeFromRuntime()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_handle.IsAllocated)
            _handle.Free();

        _pointer = IntPtr.Zero;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.CheckUsable();
    }
}
