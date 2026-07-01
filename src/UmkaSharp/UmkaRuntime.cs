using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace UmkaSharp;

/// <summary>Owns an embedded Umka interpreter instance.</summary>
public sealed class UmkaRuntime : IDisposable
{
    /// <summary>Default Umka stack size, in slots.</summary>
    public const int DefaultStackSize = 1024 * 1024;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly List<UmkaCallback> _callbacks = new();
    private readonly Dictionary<string, UmkaCallback> _callbacksByName = new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr, UmkaHostHandle> _hostHandles = new();
    private readonly Dictionary<IntPtr, UmkaNativeValue> _nativeValues = new();
    private readonly HashSet<long> _activeCallbackFrames = new();
    private readonly HashSet<string> _moduleFileNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _callbackNames = new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<string> _arguments;
    private readonly WarningSink? _warningSink;
    private long _nextCallbackFrameId;
    private bool _compileAttempted;
    private bool _compiled;
    private bool _terminated;
    private bool _disposed;
    private Exception? _lastCallbackException;

    internal IntPtr Handle { get; private set; }

    private UmkaRuntime(
        IntPtr handle,
        string sourceFileName,
        int stackSize,
        bool fileSystemEnabled,
        bool implementationLibrariesEnabled,
        IReadOnlyList<string>? arguments,
        WarningSink? warningSink)
    {
        Handle = handle;
        SourceFileName = sourceFileName;
        StackSize = stackSize;
        FileSystemEnabled = fileSystemEnabled;
        ImplementationLibrariesEnabled = implementationLibrariesEnabled;
        _arguments = Array.AsReadOnly(arguments?.ToArray() ?? Array.Empty<string>());
        _warningSink = warningSink;
    }

    /// <summary>Finalizes an instance of the <see cref="UmkaRuntime" /> class.</summary>
    ~UmkaRuntime()
    {
        Dispose(disposing: false);
    }

    /// <summary>Gets a value indicating whether the native Umka runtime can still execute code.</summary>
    public bool IsAlive
    {
        get
        {
            CheckUsable();
            if (_compileAttempted && !_compiled)
                return false;

            RefreshTerminatedState();
            return !_terminated;
        }
    }

    /// <summary>Gets a value indicating whether this runtime has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Gets the root source file name passed to Umka when the runtime was created.</summary>
    public string SourceFileName { get; }

    /// <summary>Gets the Umka stack size configured when the runtime was created, in slots.</summary>
    public int StackSize { get; }

    /// <summary>Gets a value indicating whether native Umka file-system support was enabled when the runtime was created.</summary>
    public bool FileSystemEnabled { get; }

    /// <summary>Gets a value indicating whether Umka implementation library loading was enabled when the runtime was created.</summary>
    public bool ImplementationLibrariesEnabled { get; }

    /// <summary>Gets the host-defined command-line arguments passed to Umka when the runtime was created.</summary>
    public IReadOnlyList<string> Arguments => _arguments;

    /// <summary>Gets the importable module names registered directly on this runtime.</summary>
    public IReadOnlyList<string> RegisteredModuleNames => SnapshotNames(_moduleFileNames);

    /// <summary>Gets the managed callback names registered directly on this runtime.</summary>
    public IReadOnlyList<string> RegisteredCallbackNames => SnapshotNames(_callbackNames);

    /// <summary>Gets the last managed exception thrown by any callback on this runtime, if any.</summary>
    public Exception? LastCallbackException => _lastCallbackException;

    /// <summary>Gets a value indicating whether an interrupt request is currently pending on the native runtime.</summary>
    public bool IsInterruptRequested
    {
        get
        {
            CheckUsable();
            return NativeMethods.InterruptRequested(Handle) != 0;
        }
    }

    /// <summary>Gets the observed lifecycle state of this runtime.</summary>
    public UmkaRuntimeState State =>
        _disposed
            ? UmkaRuntimeState.Disposed
            : _terminated
                ? UmkaRuntimeState.Terminated
                : _compiled
                    ? UmkaRuntimeState.Compiled
                    : _compileAttempted
                        ? UmkaRuntimeState.CompileAttempted
                        : UmkaRuntimeState.Created;

    /// <summary>Returns a diagnostic string that describes the current runtime state.</summary>
    public override string ToString() => $"UmkaRuntime({State})";

    /// <summary>Creates a runtime from an Umka source file.</summary>
    public static UmkaRuntime FromFile(
        string fileName,
        int stackSize = DefaultStackSize,
        bool fileSystemEnabled = false,
        bool implementationLibrariesEnabled = false,
        IReadOnlyList<string>? arguments = null)
    {
        return FromFile(
            fileName,
            new UmkaRuntimeOptions
            {
                StackSize = stackSize,
                FileSystemEnabled = fileSystemEnabled,
                ImplementationLibrariesEnabled = implementationLibrariesEnabled,
                Arguments = arguments,
            });
    }

    /// <summary>Creates a runtime from an Umka source file.</summary>
    public static UmkaRuntime FromFile(string fileName, UmkaRuntimeOptions? options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(fileName, nameof(fileName));
        if (!File.Exists(fileName))
            throw new FileNotFoundException("The Umka source file was not found.", fileName);

        var resolvedOptions = ValidateOptions(options);
        return Create(
            fileName,
            source: null,
            resolvedOptions.StackSize,
            resolvedOptions.FileSystemEnabled,
            resolvedOptions.ImplementationLibrariesEnabled,
            resolvedOptions.Arguments,
            resolvedOptions.WarningHandler);
    }

    /// <summary>Creates and compiles a runtime from an Umka source file.</summary>
    public static UmkaRuntime CompileFile(
        string fileName,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null) =>
        CompileCreated(FromFile(fileName, options), configure);

    /// <summary>Tries to create and compile a runtime from an Umka source file.</summary>
    public static bool TryCompileFile(
        string fileName,
        [NotNullWhen(true)] out UmkaRuntime? runtime,
        [NotNullWhen(false)] out UmkaError? error,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null) =>
        TryCompileCreated(FromFile(fileName, options), configure, out runtime, out error);

    /// <summary>Creates a runtime from an Umka source string.</summary>
    public static UmkaRuntime FromSource(
        string source,
        string fileName = "main.um",
        int stackSize = DefaultStackSize,
        bool fileSystemEnabled = false,
        bool implementationLibrariesEnabled = false,
        IReadOnlyList<string>? arguments = null)
    {
        return FromSource(
            source,
            fileName,
            new UmkaRuntimeOptions
            {
                StackSize = stackSize,
                FileSystemEnabled = fileSystemEnabled,
                ImplementationLibrariesEnabled = implementationLibrariesEnabled,
                Arguments = arguments,
            });
    }

    /// <summary>Creates a runtime from an Umka source string.</summary>
    public static UmkaRuntime FromSource(string source, UmkaRuntimeOptions? options) =>
        FromSource(source, "main.um", options);

    /// <summary>Creates a runtime from an Umka source string.</summary>
    public static UmkaRuntime FromSource(string source, string fileName, UmkaRuntimeOptions? options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(source, nameof(source));
        UmkaStringValidation.ThrowIfContainsNullCharacter(fileName, nameof(fileName));

        var resolvedOptions = ValidateOptions(options);
        return Create(
            fileName,
            source,
            resolvedOptions.StackSize,
            resolvedOptions.FileSystemEnabled,
            resolvedOptions.ImplementationLibrariesEnabled,
            resolvedOptions.Arguments,
            resolvedOptions.WarningHandler);
    }

    /// <summary>Creates and compiles a runtime from an Umka source string.</summary>
    public static UmkaRuntime CompileSource(
        string source,
        string fileName = "main.um",
        int stackSize = DefaultStackSize,
        bool fileSystemEnabled = false,
        bool implementationLibrariesEnabled = false,
        IReadOnlyList<string>? arguments = null,
        Action<UmkaRuntime>? configure = null) =>
        CompileCreated(FromSource(
            source,
            fileName,
            stackSize,
            fileSystemEnabled,
            implementationLibrariesEnabled,
            arguments),
            configure);

    /// <summary>Creates and compiles a runtime from an Umka source string.</summary>
    public static UmkaRuntime CompileSource(
        string source,
        UmkaRuntimeOptions? options,
        Action<UmkaRuntime>? configure = null) =>
        CompileSource(source, "main.um", options, configure);

    /// <summary>Creates and compiles a runtime from an Umka source string.</summary>
    public static UmkaRuntime CompileSource(
        string source,
        string fileName,
        UmkaRuntimeOptions? options,
        Action<UmkaRuntime>? configure = null) =>
        CompileCreated(FromSource(source, fileName, options), configure);

    /// <summary>Tries to create and compile a runtime from an Umka source string.</summary>
    public static bool TryCompileSource(
        string source,
        [NotNullWhen(true)] out UmkaRuntime? runtime,
        [NotNullWhen(false)] out UmkaError? error,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null) =>
        TryCompileSource(source, "main.um", out runtime, out error, options, configure);

    /// <summary>Tries to create and compile a runtime from an Umka source string.</summary>
    public static bool TryCompileSource(
        string source,
        string fileName,
        [NotNullWhen(true)] out UmkaRuntime? runtime,
        [NotNullWhen(false)] out UmkaError? error,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null) =>
        TryCompileCreated(FromSource(source, fileName, options), configure, out runtime, out error);

    /// <summary>Creates, compiles, runs, and disposes a runtime from an Umka source string.</summary>
    public static void RunSource(
        string source,
        string fileName = "main.um",
        int stackSize = DefaultStackSize,
        bool fileSystemEnabled = false,
        bool implementationLibrariesEnabled = false,
        IReadOnlyList<string>? arguments = null,
        Action<UmkaRuntime>? configure = null)
    {
        using var runtime = CompileSource(
            source,
            fileName,
            stackSize,
            fileSystemEnabled,
            implementationLibrariesEnabled,
            arguments,
            configure);
        runtime.Run();
    }

    /// <summary>Creates, compiles, runs, and disposes a runtime from an Umka source string.</summary>
    public static void RunSource(
        string source,
        UmkaRuntimeOptions? options,
        Action<UmkaRuntime>? configure = null) =>
        RunSource(source, "main.um", options, configure);

    /// <summary>Creates, compiles, runs, and disposes a runtime from an Umka source string.</summary>
    public static void RunSource(
        string source,
        string fileName,
        UmkaRuntimeOptions? options,
        Action<UmkaRuntime>? configure = null)
    {
        using var runtime = CompileSource(source, fileName, options, configure);
        runtime.Run();
    }

    /// <summary>Tries to create, compile, run, and dispose a runtime from an Umka source string.</summary>
    public static bool TryRunSource(
        string source,
        [NotNullWhen(false)] out UmkaException? exception,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null) =>
        TryRunSource(source, "main.um", out exception, options, configure);

    /// <summary>Tries to create, compile, run, and dispose a runtime from an Umka source string.</summary>
    public static bool TryRunSource(
        string source,
        string fileName,
        [NotNullWhen(false)] out UmkaException? exception,
        UmkaRuntimeOptions? options = null,
        Action<UmkaRuntime>? configure = null)
    {
        if (!TryCompileSource(source, fileName, out var runtime, out var error, options, configure))
        {
            exception = new UmkaException(error);
            return false;
        }

        using (runtime)
        {
            return runtime.TryRun(out exception);
        }
    }

    private static UmkaRuntimeOptions ValidateOptions(UmkaRuntimeOptions? options)
    {
        options ??= new UmkaRuntimeOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.StackSize);
        return options;
    }

    private static UmkaRuntime CompileCreated(UmkaRuntime runtime, Action<UmkaRuntime>? configure = null)
    {
        try
        {
            configure?.Invoke(runtime);
            runtime.Compile();
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static bool TryCompileCreated(
        UmkaRuntime createdRuntime,
        Action<UmkaRuntime>? configure,
        [NotNullWhen(true)] out UmkaRuntime? runtime,
        [NotNullWhen(false)] out UmkaError? error)
    {
        try
        {
            configure?.Invoke(createdRuntime);
            if (createdRuntime.TryCompile(out error))
            {
                runtime = createdRuntime;
                return true;
            }

            createdRuntime.Dispose();
            runtime = null;
            return false;
        }
        catch
        {
            createdRuntime.Dispose();
            throw;
        }
    }

    private static UmkaRuntime Create(
        string fileName,
        string? source,
        int stackSize,
        bool fileSystemEnabled,
        bool implementationLibrariesEnabled,
        IReadOnlyList<string>? arguments,
        Action<UmkaError>? warningHandler)
    {
        using var nativeArguments = NativeArgumentArray.Create(arguments);
        var warningSink = WarningSink.Create(warningHandler);
        var status = NativeMethods.Create(
            fileName,
            source,
            stackSize,
            nativeArguments.Count,
            nativeArguments.Pointer,
            fileSystemEnabled ? 1 : 0,
            implementationLibrariesEnabled ? 1 : 0,
            warningSink?.FunctionPointer ?? IntPtr.Zero,
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

        try
        {
            warningSink?.ThrowIfFailed();
        }
        catch
        {
            NativeMethods.Free(handle);
            throw;
        }

        return new UmkaRuntime(
            handle,
            fileName,
            stackSize,
            fileSystemEnabled,
            implementationLibrariesEnabled,
            arguments,
            warningSink);
    }

    /// <summary>Adds an importable source-string module before compilation.</summary>
    public void AddModule(string fileName, string source)
    {
        CheckUsable();
        ThrowIfCompileAttempted();
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(source);
        UmkaStringValidation.ThrowIfContainsNullCharacter(fileName, nameof(fileName));
        UmkaStringValidation.ThrowIfContainsNullCharacter(source, nameof(source));
        AddModuleCore(fileName, source, nameof(fileName));
    }

    /// <summary>Adds an importable source-file module before compilation, using the file path as the module name.</summary>
    public void AddModuleFromFile(string fileName) =>
        AddModuleFromFile(fileName, fileName);

    /// <summary>Adds an importable source-file module before compilation, using a separate logical module name.</summary>
    public void AddModuleFromFile(string moduleName, string fileName)
    {
        CheckUsable();
        ThrowIfCompileAttempted();
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(moduleName, nameof(moduleName));
        UmkaStringValidation.ThrowIfContainsNullCharacter(fileName, nameof(fileName));
        if (_moduleFileNames.Contains(moduleName))
            throw new ArgumentException($"A module named '{moduleName}' has already been added.", nameof(moduleName));
        if (!File.Exists(fileName))
            throw new FileNotFoundException("The Umka module source file was not found.", fileName);

        var source = File.ReadAllText(fileName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(source, nameof(fileName));
        AddModuleCore(moduleName, source, nameof(moduleName));
    }

    private void AddModuleCore(string fileName, string source, string fileNameParameterName)
    {
        if (_moduleFileNames.Contains(fileName))
            throw new ArgumentException($"A module named '{fileName}' has already been added.", fileNameParameterName);

        var status = NativeMethods.AddModule(Handle, fileName, source);
        ThrowIfError(status);
        _moduleFileNames.Add(fileName);
    }

    /// <summary>Registers a managed callback that can resolve an Umka prototype of the same name.</summary>
    public UmkaCallback Register(string name, UmkaCallback.CallbackFunc callback)
    {
        CheckUsable();
        ThrowIfCompileAttempted();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        return RegisterCore(name, callback);
    }

    /// <summary>Registers a managed callback for an Umka void prototype of the same name.</summary>
    public UmkaCallback RegisterVoid(string name, Action<UmkaCallFrame> callback)
    {
        CheckUsable();
        ThrowIfCompileAttempted();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        return RegisterCore(
            name,
            frame =>
            {
                callback(frame);
                return UmkaValue.Void;
            });
    }

    /// <summary>Gets a managed callback registered directly on this runtime.</summary>
    public UmkaCallback GetCallback(string name)
    {
        if (TryGetCallback(name, out var callback))
            return callback;

        throw new KeyNotFoundException($"Callback '{name}' was not registered on this runtime.");
    }

    /// <summary>Tries to get a managed callback registered directly on this runtime.</summary>
    public bool TryGetCallback(string name, [NotNullWhen(true)] out UmkaCallback? callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        UmkaStringValidation.ThrowIfContainsNullCharacter(name, nameof(name));
        return _callbacksByName.TryGetValue(name, out callback);
    }

    private UmkaCallback RegisterCore(string name, UmkaCallback.CallbackFunc callback)
    {
        UmkaStringValidation.ThrowIfContainsNullCharacter(name, nameof(name));
        if (_callbackNames.Contains(name))
            throw new ArgumentException($"A callback named '{name}' has already been registered.", nameof(name));

        var registered = new UmkaCallback(this, name, callback);
        try
        {
            var fnPtr = Marshal.GetFunctionPointerForDelegate(registered.NativeDelegate);
            var status = NativeMethods.AddCallback(Handle, name, fnPtr, IntPtr.Zero, out var slot);
            if (status != 0)
            {
                var error = UmkaError.FromNative(Handle);
                if (IsEmptyNativeError(error))
                    throw new ArgumentException($"A callback named '{name}' has already been registered or is reserved by Umka.", nameof(name));

                throw new UmkaException(error);
            }

            registered.SetNativeSlot(slot);
            _callbacks.Add(registered);
            _callbackNames.Add(name);
            _callbacksByName.Add(name, registered);
            return registered;
        }
        catch
        {
            registered.Dispose();
            throw;
        }
    }

    /// <summary>Creates a runtime-owned opaque handle to a managed host object.</summary>
    public UmkaHostHandle CreateHostHandle(object target)
    {
        CheckUsable();
        ThrowIfCompileFailed();
        ThrowIfTerminated();
        ArgumentNullException.ThrowIfNull(target);

        var handle = new UmkaHostHandle(this, target);
        _hostHandles.Add(handle.RawPointer, handle);
        return handle;
    }

    /// <summary>Gets a runtime-owned managed host object from an opaque handle pointer.</summary>
    public T GetHostObject<T>(IntPtr handleAddress) => GetHostObjectCore<T>(handleAddress);

    /// <summary>Tries to get a runtime-owned managed host object from an opaque handle pointer.</summary>
    public bool TryGetHostObject<T>(IntPtr handleAddress, [NotNullWhen(true)] out T? target)
    {
        CheckUsable();
        target = default;
        if (handleAddress == IntPtr.Zero)
            return false;

        if (!_hostHandles.TryGetValue(handleAddress, out var handle))
            return false;

        if (handle.RawTarget is not T typed)
            return false;

        target = typed;
        return true;
    }

    /// <summary>Requests that the native Umka runtime stop execution at the next interrupt check.</summary>
    /// <remarks>This method may be called from another managed thread while the owning thread is executing Umka code.</remarks>
    public void RequestInterrupt(string? message = null)
    {
        ObjectDisposedException.ThrowIf(_disposed || Handle == IntPtr.Zero, this);
        UmkaStringValidation.ThrowIfContainsNullCharacter(message, nameof(message));
        NativeMethods.RequestInterrupt(Handle, message);
    }

    /// <summary>Clears a pending native Umka interrupt request.</summary>
    public void ClearInterrupt()
    {
        CheckUsable();
        NativeMethods.ClearInterrupt(Handle);
    }

    /// <summary>Compiles the loaded main source and all imported modules.</summary>
    public void Compile()
    {
        if (!TryCompile(out var error))
            throw new UmkaException(error);
    }

    /// <summary>Tries to compile the loaded main source and all imported modules.</summary>
    public bool TryCompile([NotNullWhen(false)] out UmkaError? error)
    {
        CheckUsable();
        ThrowIfCompileAttempted();
        _compileAttempted = true;
        _warningSink?.ClearException();
        var status = NativeMethods.Compile(Handle);
        if (status == 0)
            _compiled = true;

        if (status != 0)
        {
            error = UmkaError.FromNative(Handle);
            return false;
        }

        _warningSink?.ThrowIfFailed();
        _compiled = true;
        error = null;
        return true;
    }

    /// <summary>Runs the compiled program's main function, if present.</summary>
    public void Run()
    {
        if (!TryRun(out var exception))
            throw exception;
    }

    /// <summary>Tries to run the compiled program's main function, if present.</summary>
    public bool TryRun([NotNullWhen(false)] out UmkaException? exception)
    {
        CheckCallable();
        ClearLastCallbackException();
        var status = NativeMethods.Run(Handle);
        if (status == 0)
        {
            RefreshTerminatedState();
            exception = null;
            return true;
        }

        var error = UmkaError.FromNative(Handle);
        var innerException = _lastCallbackException;
        RefreshTerminatedState();
        exception = new UmkaException(error, innerException);
        return false;
    }

    /// <summary>Gets an exported Umka function that can be called from C#.</summary>
    public UmkaFunction GetFunction(string functionName, string? moduleName = null)
    {
        if (TryGetFunction(functionName, out var function, moduleName))
            return function;

        throw new UmkaException(CreateFunctionNotFoundError(functionName, moduleName));
    }

    /// <summary>Tries to get an exported root-source Umka function that can be called from C#.</summary>
    public bool TryGetFunction(
        string functionName,
        [NotNullWhen(true)] out UmkaFunction? function) =>
        TryGetFunction(functionName, out function, moduleName: null);

    /// <summary>Tries to get an exported Umka module function that can be called from C#.</summary>
    public bool TryGetFunction(
        string functionName,
        string moduleName,
        [NotNullWhen(true)] out UmkaFunction? function) =>
        TryGetFunction(functionName, out function, moduleName);

    /// <summary>Tries to get an exported Umka function that can be called from C#.</summary>
    public bool TryGetFunction(
        string functionName,
        [NotNullWhen(true)] out UmkaFunction? function,
        string? moduleName = null)
    {
        CheckCompiled();
        ThrowIfTerminated();
        ArgumentException.ThrowIfNullOrWhiteSpace(functionName);
        UmkaStringValidation.ThrowIfContainsNullCharacter(functionName, nameof(functionName));
        if (moduleName is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
            UmkaStringValidation.ThrowIfContainsNullCharacter(moduleName, nameof(moduleName));
        }

        function = null;

        var status = NativeMethods.GetFunction(Handle, moduleName, functionName, out var context);
        if (status != 0)
        {
            var error = UmkaError.FromNative(Handle);
            if (status == NativeMethods.NotFoundStatus || IsEmptyNativeError(error))
                return false;

            throw new UmkaException(error);
        }

        if (context.EntryOffset <= 0)
            return false;

        function = CreateFunctionFromContext(functionName, moduleName, context);
        return true;
    }

    internal UmkaFunction CreateCallableFunction(UmkaNativeValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        CheckCallable();

        if (!ReferenceEquals(value.Runtime, this))
        {
            throw new InvalidOperationException(
                $"Retained native value of Umka type '{value.Type.TypeName}' belongs to a different runtime.");
        }

        if (value.Type.Kind == UmkaTypeKind.Fiber)
        {
            throw new NotSupportedException(
                "Umka fiber values cannot be invoked from C# because the native API does not expose safe host-side fiber resume or ownership semantics.");
        }

        if (!value.Type.IsCallable)
        {
            throw new NotSupportedException(
                $"Retained native value of Umka type '{value.Type.TypeName}' is not an Umka callable value.");
        }

        if (NativeMethods.NativeValueCallableValid(value.Handle) == 0)
        {
            throw new InvalidOperationException(
                $"Retained native value of Umka type '{value.Type.TypeName}' is not a valid callable value.");
        }

        var status = NativeMethods.NativeValueMakeCallableContext(Handle, value.Handle, out var context);
        if (status != 0 || context.EntryOffset <= 0)
        {
            throw new InvalidOperationException(
                $"Retained native value of Umka type '{value.Type.TypeName}' could not be converted to a callable context.");
        }

        return CreateFunctionFromContext("<callable>", moduleName: null, context, value);
    }

    private UmkaFunction CreateFunctionFromContext(
        string functionName,
        string? moduleName,
        NativeMethods.FunctionContext context,
        UmkaNativeValue? callableValue = null)
    {
        var parameterCount = NativeMethods.ContextGetArgumentCount(ref context);
        var parameterTypes = new UmkaTypeInfo[parameterCount];
        var nativeParameterKinds = new NativeUmkaTypeKind[parameterCount];
        var nativeParameterElementKinds = new NativeUmkaTypeKind[parameterCount];
        for (var i = 0; i < parameterCount; i++)
        {
            var kind = NativeMethods.ContextGetParameterKind(ref context, i);
            nativeParameterKinds[i] = kind;
            nativeParameterElementKinds[i] = NativeMethods.ContextGetParameterElementKind(ref context, i);
            parameterTypes[i] = CreateContextParameterTypeInfo(ref context, i, kind);
        }

        var resultKind = NativeMethods.ContextGetResultKind(ref context);
        var resultType = CreateContextResultTypeInfo(ref context, resultKind);
        var requiredParameterCount = NativeMethods.ContextGetRequiredArgumentCount(ref context);
        var defaultParameterCount = NativeMethods.ContextGetDefaultArgumentCount(ref context);

        return new UmkaFunction(
            this,
            functionName,
            moduleName,
            context,
            parameterTypes,
            nativeParameterKinds,
            nativeParameterElementKinds,
            requiredParameterCount,
            defaultParameterCount,
            resultType,
            callableValue);
    }

    private static UmkaTypeInfo CreateContextParameterTypeInfo(
        ref NativeMethods.FunctionContext context,
        int index,
        NativeUmkaTypeKind kind)
    {
        var isEnum = NativeMethods.ContextGetParameterIsEnum(ref context, index) != 0;
        return UmkaTypeInfoFactory.Create(
            kind,
            NativeMethods.ContextGetParameterTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterSize(ref context, index),
            NativeMethods.ContextGetParameterItemCount(ref context, index),
            NativeMethods.ContextGetParameterHasReferences(ref context, index) != 0,
            isEnum,
            isEnum ? GetContextParameterEnumMembers(ref context, index) : Array.Empty<UmkaEnumMemberInfo>(),
            NativeMethods.ContextGetParameterElementKind(ref context, index),
            NativeMethods.ContextGetParameterElementTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterElementSize(ref context, index),
            NativeMethods.ContextGetParameterElementHasReferences(ref context, index) != 0,
            NativeMethods.ContextGetParameterNestedElementKind(ref context, index),
            NativeMethods.ContextGetParameterNestedElementTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterNestedElementSize(ref context, index),
            NativeMethods.ContextGetParameterNestedElementHasReferences(ref context, index) != 0,
            NativeMethods.ContextGetParameterMapKeyKind(ref context, index),
            NativeMethods.ContextGetParameterMapKeyTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterMapKeySize(ref context, index),
            NativeMethods.ContextGetParameterMapKeyHasReferences(ref context, index) != 0,
            NativeMethods.ContextGetParameterMapValueKind(ref context, index),
            NativeMethods.ContextGetParameterMapValueTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterMapValueSize(ref context, index),
            NativeMethods.ContextGetParameterMapValueHasReferences(ref context, index) != 0,
            NativeMethods.ContextGetParameterMapValueElementKind(ref context, index),
            NativeMethods.ContextGetParameterMapValueElementTypeName(ref context, index).ToManagedString(),
            NativeMethods.ContextGetParameterMapValueElementSize(ref context, index),
            NativeMethods.ContextGetParameterMapValueElementHasReferences(ref context, index) != 0,
            NativeMethods.ContextGetParameterIsVariadicParameterList(ref context, index) != 0);
    }

    private static UmkaTypeInfo CreateContextResultTypeInfo(
        ref NativeMethods.FunctionContext context,
        NativeUmkaTypeKind kind)
    {
        var isEnum = NativeMethods.ContextGetResultIsEnum(ref context) != 0;
        return UmkaTypeInfoFactory.Create(
            kind,
            NativeMethods.ContextGetResultTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultSize(ref context),
            NativeMethods.ContextGetResultItemCount(ref context),
            NativeMethods.ContextGetResultHasReferences(ref context) != 0,
            isEnum,
            isEnum ? GetContextResultEnumMembers(ref context) : Array.Empty<UmkaEnumMemberInfo>(),
            NativeMethods.ContextGetResultElementKind(ref context),
            NativeMethods.ContextGetResultElementTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultElementSize(ref context),
            NativeMethods.ContextGetResultElementHasReferences(ref context) != 0,
            NativeMethods.ContextGetResultNestedElementKind(ref context),
            NativeMethods.ContextGetResultNestedElementTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultNestedElementSize(ref context),
            NativeMethods.ContextGetResultNestedElementHasReferences(ref context) != 0,
            NativeMethods.ContextGetResultMapKeyKind(ref context),
            NativeMethods.ContextGetResultMapKeyTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultMapKeySize(ref context),
            NativeMethods.ContextGetResultMapKeyHasReferences(ref context) != 0,
            NativeMethods.ContextGetResultMapValueKind(ref context),
            NativeMethods.ContextGetResultMapValueTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultMapValueSize(ref context),
            NativeMethods.ContextGetResultMapValueHasReferences(ref context) != 0,
            NativeMethods.ContextGetResultMapValueElementKind(ref context),
            NativeMethods.ContextGetResultMapValueElementTypeName(ref context).ToManagedString(),
            NativeMethods.ContextGetResultMapValueElementSize(ref context),
            NativeMethods.ContextGetResultMapValueElementHasReferences(ref context) != 0,
            NativeMethods.ContextGetResultIsVariadicParameterList(ref context) != 0);
    }

    private static UmkaEnumMemberInfo[] GetContextParameterEnumMembers(
        ref NativeMethods.FunctionContext context,
        int index)
    {
        var count = NativeMethods.ContextGetParameterEnumMemberCount(ref context, index);
        if (count <= 0)
            return Array.Empty<UmkaEnumMemberInfo>();

        var members = new UmkaEnumMemberInfo[count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new UmkaEnumMemberInfo(
                NativeMethods.ContextGetParameterEnumMemberName(ref context, index, i).ToManagedString() ?? "unknown",
                NativeMethods.ContextGetParameterEnumMemberSignedValue(ref context, index, i),
                NativeMethods.ContextGetParameterEnumMemberUnsignedValue(ref context, index, i));
        }

        return members;
    }

    private static UmkaEnumMemberInfo[] GetContextResultEnumMembers(ref NativeMethods.FunctionContext context)
    {
        var count = NativeMethods.ContextGetResultEnumMemberCount(ref context);
        if (count <= 0)
            return Array.Empty<UmkaEnumMemberInfo>();

        var members = new UmkaEnumMemberInfo[count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new UmkaEnumMemberInfo(
                NativeMethods.ContextGetResultEnumMemberName(ref context, i).ToManagedString() ?? "unknown",
                NativeMethods.ContextGetResultEnumMemberSignedValue(ref context, i),
                NativeMethods.ContextGetResultEnumMemberUnsignedValue(ref context, i));
        }

        return members;
    }

    /// <summary>Gets the last error reported by the Umka runtime.</summary>
    public UmkaError GetLastError()
    {
        CheckUsable();
        return UmkaError.FromNative(Handle);
    }

    /// <summary>Tries to get the last meaningful error reported by the Umka runtime.</summary>
    public bool TryGetLastError([NotNullWhen(true)] out UmkaError? error)
    {
        CheckUsable();
        error = UmkaError.FromNative(Handle);
        if (!IsEmptyNativeError(error))
            return true;

        error = null;
        return false;
    }

    internal void ThrowIfError(int status)
    {
        if (status == 0)
            return;

        throw new UmkaException(UmkaError.FromNative(Handle));
    }

    internal void ThrowIfExecutionError(int status)
    {
        if (status == 0)
        {
            RefreshTerminatedState();
            return;
        }

        var error = UmkaError.FromNative(Handle);
        var innerException = _lastCallbackException;
        RefreshTerminatedState();
        throw new UmkaException(error, innerException);
    }

    internal void SetLastCallbackException(Exception exception)
    {
        _lastCallbackException = exception;
    }

    internal void ClearLastCallbackException()
    {
        _lastCallbackException = null;
    }

    internal long BeginCallbackFrame()
    {
        CheckUsable();
        var frameId = checked(++_nextCallbackFrameId);
        _activeCallbackFrames.Add(frameId);
        return frameId;
    }

    internal void EndCallbackFrame(long frameId)
    {
        _activeCallbackFrames.Remove(frameId);
    }

    internal void CheckCallbackFrameActive(long frameId)
    {
        CheckUsable();
        if (!_activeCallbackFrames.Contains(frameId))
            throw new InvalidOperationException("Umka callback frame is no longer active.");
    }

    private static bool IsEmptyNativeError(UmkaError error) =>
        error.Code == 0 &&
        string.IsNullOrWhiteSpace(error.Message) &&
        string.IsNullOrWhiteSpace(error.FileName) &&
        string.IsNullOrWhiteSpace(error.FunctionName);

    private static string FormatFunctionNotFound(string functionName, string? moduleName) =>
        string.IsNullOrWhiteSpace(moduleName)
            ? $"Function '{functionName}' was not found."
            : $"Function '{functionName}' was not found in module '{moduleName}'.";

    private static UmkaError CreateFunctionNotFoundError(string functionName, string? moduleName) =>
        new(moduleName, functionName, 0, 0, NativeMethods.NotFoundStatus, FormatFunctionNotFound(functionName, moduleName));

    private static ReadOnlyCollection<string> SnapshotNames(IEnumerable<string> names) =>
        Array.AsReadOnly(names.OrderBy(name => name, StringComparer.Ordinal).ToArray());

    internal void CheckUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"UmkaRuntime must be used from its owning thread {_ownerThreadId}. Current thread: {Environment.CurrentManagedThreadId}.");
    }

    internal void CheckCallable()
    {
        CheckCompiled();
        ThrowIfTerminated();
    }

    internal void ReleaseHostHandle(UmkaHostHandle handle)
    {
        CheckUsable();
        if (_hostHandles.Remove(handle.RawPointer))
            handle.DisposeFromRuntime();
    }

    internal UmkaNativeValue AdoptNativeValue(IntPtr handle, UmkaTypeInfo type)
    {
        CheckUsable();
        if (handle == IntPtr.Zero)
            throw new UmkaException("Native Umka value retention returned a null handle.");

        var value = new UmkaNativeValue(this, type, handle);
        _nativeValues.Add(handle, value);
        return value;
    }

    internal UmkaAnyValue InspectAnyValue(UmkaNativeValue value)
    {
        CheckUsable();
        ObjectDisposedException.ThrowIf(value.IsDisposed, value);
        if (!ReferenceEquals(value.Runtime, this))
            throw new InvalidOperationException("The native Umka value is not owned by this runtime.");
        if (!value.Type.IsAny)
            throw new InvalidOperationException($"Retained native Umka value type '{value.Type.TypeName}' is not Umka any.");

        var state = NativeMethods.NativeValueAnyState(value.Handle);
        if (state == 1)
            return UmkaAnyValue.Null;
        if (state != 2)
            throw new InvalidOperationException($"Retained native Umka value type '{value.Type.TypeName}' could not be deconstructed as Umka any.");
        if (NativeMethods.NativeValueAnySelfState(value.Handle) == 0)
            throw new InvalidOperationException($"Retained native Umka value type '{value.Type.TypeName}' did not expose a concrete any payload pointer.");

        var nativeKind = NativeMethods.NativeValueAnyGetPayloadKind(value.Handle);
        var payloadType = CreateAnyPayloadTypeInfo(value.Handle, nativeKind);
        var payload = nativeKind switch
        {
            NativeUmkaTypeKind.Int8
                or NativeUmkaTypeKind.Int16
                or NativeUmkaTypeKind.Int32
                or NativeUmkaTypeKind.Int => UmkaValue.From(NativeMethods.NativeValueAnyGetPayloadInt(value.Handle)),
            NativeUmkaTypeKind.UInt8
                or NativeUmkaTypeKind.UInt16
                or NativeUmkaTypeKind.UInt32
                or NativeUmkaTypeKind.UInt => UmkaValue.From(NativeMethods.NativeValueAnyGetPayloadUInt(value.Handle)),
            NativeUmkaTypeKind.Char => CreateAnyCharacterPayload(value.Handle),
            NativeUmkaTypeKind.Bool => UmkaValue.From(NativeMethods.NativeValueAnyGetPayloadInt(value.Handle) != 0),
            NativeUmkaTypeKind.Real32
                or NativeUmkaTypeKind.Real => UmkaValue.From(NativeMethods.NativeValueAnyGetPayloadReal(value.Handle)),
            NativeUmkaTypeKind.String => UmkaValue.From(NativeMethods.NativeValueAnyGetPayloadString(value.Handle).ToManagedString()),
            NativeUmkaTypeKind.Pointer => UmkaValue.FromPointer(NativeMethods.NativeValueAnyGetPayloadPointer(value.Handle)),
            NativeUmkaTypeKind.WeakPointer => UmkaValue.FromWeakPointer(NativeMethods.NativeValueAnyGetPayloadUInt(value.Handle)),
            NativeUmkaTypeKind.StaticArray
                or NativeUmkaTypeKind.DynamicArray
                or NativeUmkaTypeKind.Map
                or NativeUmkaTypeKind.Struct
                or NativeUmkaTypeKind.Interface
                or NativeUmkaTypeKind.Closure
                or NativeUmkaTypeKind.Function => RetainAnyPayload(value, payloadType),
            NativeUmkaTypeKind.Fiber => throw new NotSupportedException(
                "Umka any payloads containing fiber values cannot be retained or invoked from C# because the native runtime does not expose host-side fiber resume ownership semantics."),
            _ => throw new NotSupportedException(
                $"Umka any payload type '{payloadType.TypeName}' is not supported by UmkaSharp.")
        };

        return UmkaAnyValue.FromInspected(payloadType, payload);
    }

    internal void ReleaseNativeValue(UmkaNativeValue value)
    {
        CheckUsable();
        if (_nativeValues.Remove(value.Handle))
            value.DisposeFromRuntime();
    }

    private UmkaValue RetainAnyPayload(UmkaNativeValue anyValue, UmkaTypeInfo payloadType)
    {
        var status = NativeMethods.NativeValueAnyRetainPayload(Handle, anyValue.Handle, out var payloadHandle);
        if (status != 0 || payloadHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Umka any payload type '{payloadType.TypeName}' could not be retained as a native Umka value.");
        }

        return UmkaValue.FromNativeValue(AdoptNativeValue(payloadHandle, payloadType));
    }

    private static UmkaValue CreateAnyCharacterPayload(IntPtr anyHandle)
    {
        var value = NativeMethods.NativeValueAnyGetPayloadUInt(anyHandle);
        if (value > byte.MaxValue)
            throw new OverflowException($"Umka any char payload value {value} is outside the Umka char range.");

        return UmkaValue.From((char)value);
    }

    private static UmkaTypeInfo CreateAnyPayloadTypeInfo(IntPtr anyHandle, NativeUmkaTypeKind nativeKind)
    {
        var isEnum = NativeMethods.NativeValueAnyGetPayloadIsEnum(anyHandle) != 0;
        return UmkaTypeInfoFactory.Create(
            nativeKind,
            NativeMethods.NativeValueAnyGetPayloadTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadSize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadItemCount(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadHasReferences(anyHandle) != 0,
            isEnum,
            isEnum ? GetAnyPayloadEnumMembers(anyHandle) : Array.Empty<UmkaEnumMemberInfo>(),
            NativeMethods.NativeValueAnyGetPayloadElementKind(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadElementTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadElementSize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadElementHasReferences(anyHandle) != 0,
            NativeMethods.NativeValueAnyGetPayloadNestedElementKind(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadNestedElementTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadNestedElementSize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadNestedElementHasReferences(anyHandle) != 0,
            NativeMethods.NativeValueAnyGetPayloadMapKeyKind(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapKeyTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadMapKeySize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapKeyHasReferences(anyHandle) != 0,
            NativeMethods.NativeValueAnyGetPayloadMapValueKind(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapValueTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadMapValueSize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapValueHasReferences(anyHandle) != 0,
            NativeMethods.NativeValueAnyGetPayloadMapValueElementKind(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapValueElementTypeName(anyHandle).ToManagedString(),
            NativeMethods.NativeValueAnyGetPayloadMapValueElementSize(anyHandle),
            NativeMethods.NativeValueAnyGetPayloadMapValueElementHasReferences(anyHandle) != 0,
            NativeMethods.NativeValueAnyGetPayloadIsVariadicParameterList(anyHandle) != 0);
    }

    private static UmkaEnumMemberInfo[] GetAnyPayloadEnumMembers(IntPtr anyHandle)
    {
        var count = NativeMethods.NativeValueAnyGetPayloadEnumMemberCount(anyHandle);
        if (count <= 0)
            return Array.Empty<UmkaEnumMemberInfo>();

        var members = new UmkaEnumMemberInfo[count];
        for (var i = 0; i < members.Length; i++)
        {
            members[i] = new UmkaEnumMemberInfo(
                NativeMethods.NativeValueAnyGetPayloadEnumMemberName(anyHandle, i).ToManagedString() ?? "unknown",
                NativeMethods.NativeValueAnyGetPayloadEnumMemberSignedValue(anyHandle, i),
                NativeMethods.NativeValueAnyGetPayloadEnumMemberUnsignedValue(anyHandle, i));
        }

        return members;
    }

    private T GetHostObjectCore<T>(IntPtr handleAddress)
    {
        CheckUsable();
        if (handleAddress == IntPtr.Zero)
            throw new InvalidOperationException("Host object handle pointer is null.");

        if (!_hostHandles.TryGetValue(handleAddress, out var handle))
            throw new InvalidOperationException("Host object handle is not owned by this runtime or has already been disposed.");

        var target = handle.RawTarget;
        return target is T typed
            ? typed
            : throw new InvalidCastException(
                $"Host handle target type {target.GetType().FullName} cannot be read as {typeof(T).FullName}.");
    }

    private void CheckCompiled()
    {
        CheckUsable();
        if (!_compiled)
            throw new InvalidOperationException("Compile() must be called before running or calling Umka functions.");
    }

    private void ThrowIfCompileAttempted()
    {
        if (_compileAttempted)
            throw new InvalidOperationException("This operation must happen before Compile() is called.");
    }

    private void ThrowIfCompileFailed()
    {
        if (_compileAttempted && !_compiled)
            throw new InvalidOperationException("This operation cannot be used after compilation failed.");
    }

    private void ThrowIfTerminated()
    {
        RefreshTerminatedState();
        if (_terminated)
            throw new InvalidOperationException("The Umka runtime has terminated and cannot execute more code.");
    }

    private void RefreshTerminatedState()
    {
        if (!_terminated && Handle != IntPtr.Zero && NativeMethods.Alive(Handle) == 0)
            _terminated = true;
    }

    /// <summary>Releases the native Umka runtime and all callbacks and host handles owned by it.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            foreach (var nativeValue in _nativeValues.Values)
                nativeValue.DisposeFromRuntime();
            _nativeValues.Clear();

            NativeMethods.ClearWarningCallback(Handle);

            // Umka's free path expects compile-time VM initialization for some sources.
            if (!_compileAttempted)
                _ = NativeMethods.Compile(Handle);

            NativeMethods.Free(Handle);
            Handle = IntPtr.Zero;
        }

        foreach (var callback in _callbacks)
            callback.Dispose();
        _callbacks.Clear();

        foreach (var hostHandle in _hostHandles.Values)
            hostHandle.DisposeFromRuntime();
        _hostHandles.Clear();
    }

    private sealed class NativeArgumentArray : IDisposable
    {
        private readonly IntPtr[] _strings;

        private NativeArgumentArray(IntPtr[] strings, IntPtr pointer)
        {
            _strings = strings;
            Pointer = pointer;
            Count = strings.Length;
        }

        public int Count { get; }

        public IntPtr Pointer { get; }

        public static NativeArgumentArray Create(IReadOnlyList<string>? arguments)
        {
            if (arguments is null || arguments.Count == 0)
                return new NativeArgumentArray(Array.Empty<IntPtr>(), IntPtr.Zero);

            var strings = new IntPtr[arguments.Count];
            var pointer = IntPtr.Zero;
            try
            {
                for (var i = 0; i < arguments.Count; i++)
                {
                    var argument = arguments[i];
                    if (argument is null)
                        throw new ArgumentException("Command-line arguments cannot contain null values.", nameof(arguments));
                    UmkaStringValidation.ThrowIfContainsNullCharacter(argument, nameof(arguments));

                    strings[i] = Marshal.StringToCoTaskMemUTF8(argument);
                }

                pointer = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
                Marshal.Copy(strings, 0, pointer, strings.Length);
                return new NativeArgumentArray(strings, pointer);
            }
            catch
            {
                foreach (var value in strings)
                    Marshal.FreeCoTaskMem(value);
                Marshal.FreeHGlobal(pointer);
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var value in _strings)
                Marshal.FreeCoTaskMem(value);
            Marshal.FreeHGlobal(Pointer);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class WarningSink
    {
        private readonly Action<UmkaError> _handler;
        private readonly NativeMethods.ManagedWarningCallback _callback;
        private Exception? _exception;

        private WarningSink(Action<UmkaError> handler)
        {
            _handler = handler;
            _callback = Invoke;
            FunctionPointer = Marshal.GetFunctionPointerForDelegate(_callback);
        }

        public IntPtr FunctionPointer { get; }

        public static WarningSink? Create(Action<UmkaError>? handler) =>
            handler is null ? null : new WarningSink(handler);

        public void ClearException()
        {
            _exception = null;
        }

        public void ThrowIfFailed()
        {
            if (_exception is not null)
                throw new InvalidOperationException("Umka warning handler failed.", _exception);
        }

        private void Invoke(IntPtr warning)
        {
            try
            {
                _handler(UmkaError.FromNativeReport(warning));
            }
            catch (Exception ex)
            {
                _exception ??= ex;
            }
        }
    }
}
