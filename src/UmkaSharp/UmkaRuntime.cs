using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Owns an embedded Umka interpreter instance.</summary>
public sealed class UmkaRuntime : IDisposable
{
    /// <summary>Default Umka stack size, in slots.</summary>
    public const int DefaultStackSize = 1024 * 1024;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly List<UmkaCallback> _callbacks = new();
    private bool _compiled;
    private bool _disposed;

    internal IntPtr Handle { get; private set; }

    private UmkaRuntime(IntPtr handle)
    {
        Handle = handle;
    }

    /// <summary>Creates a runtime from an Umka source string.</summary>
    public static UmkaRuntime FromSource(
        string source,
        string fileName = "main.um",
        int stackSize = DefaultStackSize,
        bool fileSystemEnabled = false,
        bool implementationLibrariesEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var status = NativeMethods.Create(
            fileName,
            source,
            stackSize,
            fileSystemEnabled ? 1 : 0,
            implementationLibrariesEnabled ? 1 : 0,
            out var handle);

        if (status != 0)
        {
            var error = handle == IntPtr.Zero
                ? new UmkaError(fileName, null, 0, 0, status, "Failed to initialize Umka.")
                : UmkaError.FromNative(handle);
            if (handle != IntPtr.Zero)
                NativeMethods.Free(handle);
            throw new UmkaException(error);
        }

        return new UmkaRuntime(handle);
    }

    /// <summary>Adds an importable source-string module before compilation.</summary>
    public void AddModule(string fileName, string source)
    {
        CheckUsable();
        ThrowIfCompiled();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(source);

        var status = NativeMethods.AddModule(Handle, fileName, source);
        ThrowIfError(status);
    }

    /// <summary>Registers a managed callback that can resolve an Umka prototype of the same name.</summary>
    public UmkaCallback Register(string name, UmkaCallback.CallbackFunc callback)
    {
        CheckUsable();
        ThrowIfCompiled();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);

        var registered = new UmkaCallback(callback);
        try
        {
            var fnPtr = Marshal.GetFunctionPointerForDelegate(registered.NativeDelegate);
            var status = NativeMethods.AddCallback(Handle, name, fnPtr, IntPtr.Zero, out var slot);
            ThrowIfError(status);
            registered.SetNativeSlot(slot);
            _callbacks.Add(registered);
            return registered;
        }
        catch
        {
            registered.Dispose();
            throw;
        }
    }

    /// <summary>Compiles the loaded main source and all imported modules.</summary>
    public void Compile()
    {
        CheckUsable();
        ThrowIfCompiled();
        var status = NativeMethods.Compile(Handle);
        ThrowIfError(status);
        _compiled = true;
    }

    /// <summary>Runs the compiled program's main function, if present.</summary>
    public void Run()
    {
        CheckCompiled();
        var status = NativeMethods.Run(Handle);
        ThrowIfError(status);
    }

    /// <summary>Gets an exported Umka function that can be called from C#.</summary>
    public UmkaFunction GetFunction(string functionName, string? moduleName = null)
    {
        CheckCompiled();
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);

        var status = NativeMethods.GetFunction(Handle, moduleName, functionName, out var context);
        ThrowIfError(status);

        if (context.EntryOffset <= 0)
            throw new UmkaException($"Function '{functionName}' was not found.");

        return new UmkaFunction(this, functionName, context);
    }

    /// <summary>Gets the last error reported by the Umka runtime.</summary>
    public UmkaError GetLastError()
    {
        CheckUsable();
        return UmkaError.FromNative(Handle);
    }

    internal void ThrowIfError(int status)
    {
        if (status == 0)
            return;

        throw new UmkaException(UmkaError.FromNative(Handle));
    }

    internal void CheckUsable()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UmkaRuntime));
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"UmkaRuntime must be used from its owning thread {_ownerThreadId}. Current thread: {Environment.CurrentManagedThreadId}.");
    }

    private void CheckCompiled()
    {
        CheckUsable();
        if (!_compiled)
            throw new InvalidOperationException("Compile() must be called before running or calling Umka functions.");
    }

    private void ThrowIfCompiled()
    {
        if (_compiled)
            throw new InvalidOperationException("This operation must happen before Compile().");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            NativeMethods.Free(Handle);
            Handle = IntPtr.Zero;
        }

        foreach (var callback in _callbacks)
            callback.Dispose();
        _callbacks.Clear();
    }
}
