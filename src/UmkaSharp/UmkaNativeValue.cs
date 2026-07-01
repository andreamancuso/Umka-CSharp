namespace UmkaSharp;

using System.Diagnostics.CodeAnalysis;

/// <summary>Represents a runtime-owned retained Umka value.</summary>
public sealed class UmkaNativeValue : IDisposable
{
    private readonly UmkaRuntime _runtime;
    private IntPtr _handle;
    private bool _disposed;

    internal UmkaNativeValue(UmkaRuntime runtime, UmkaTypeInfo type, IntPtr handle)
    {
        _runtime = runtime;
        Type = type;
        _handle = handle;
    }

    /// <summary>Gets the retained Umka value type metadata.</summary>
    public UmkaTypeInfo Type { get; }

    /// <summary>Gets a value indicating whether the retained native value has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets a value indicating whether the retained value type is an Umka callable <c>fn</c> or closure.</summary>
    public bool IsCallable => Type.IsCallable;

    internal IntPtr Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    internal UmkaRuntime Runtime => _runtime;

    /// <summary>Creates an Umka value that can pass this retained native value back to the owning runtime.</summary>
    public UmkaValue ToValue()
    {
        ThrowIfDisposed();
        return UmkaValue.FromNativeValue(this);
    }

    /// <summary>Deconstructs this retained value as an Umka <c>any</c> value.</summary>
    public UmkaAnyValue AsAny()
    {
        ThrowIfDisposed();
        return _runtime.InspectAnyValue(this);
    }

    /// <summary>Tries to deconstruct this retained value as an Umka <c>any</c> value.</summary>
    public bool TryAsAny([NotNullWhen(true)] out UmkaAnyValue? value)
    {
        try
        {
            value = AsAny();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (UmkaException)
        {
        }

        value = null;
        return false;
    }

    /// <summary>Creates an invocable function wrapper over this retained Umka callable value.</summary>
    public UmkaFunction AsCallable()
    {
        ThrowIfDisposed();
        return _runtime.CreateCallableFunction(this);
    }

    /// <summary>Tries to create an invocable function wrapper over this retained Umka callable value.</summary>
    public bool TryAsCallable([NotNullWhen(true)] out UmkaFunction? function)
    {
        try
        {
            function = AsCallable();
            return true;
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (UmkaException)
        {
        }

        function = null;
        return false;
    }

    /// <summary>Returns a diagnostic string that includes the retained type and disposal state.</summary>
    public override string ToString() =>
        $"UmkaNativeValue(Type={Type.TypeName}, {(_disposed ? "Disposed" : "Alive")})";

    /// <summary>Releases the retained native Umka value.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _runtime.ReleaseNativeValue(this);
        GC.SuppressFinalize(this);
    }

    internal bool CanAssignTo(UmkaRuntime runtime, UmkaTypeInfo targetType) =>
        !_disposed &&
        ReferenceEquals(_runtime, runtime) &&
        (Type.IsEquivalentTo(targetType) ||
            (targetType.Kind == UmkaTypeKind.Interface &&
                !targetType.IsAny &&
                Type.Kind == UmkaTypeKind.Struct));

    internal void DisposeFromRuntime()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.NativeValueRelease(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _runtime.CheckUsable();
    }
}
