using System.Runtime.CompilerServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeLifecycleTests
{
    [Fact]
    public void Runtime_rejects_run_and_lookup_before_compile()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.Throws<InvalidOperationException>(() => runtime.Run());
        Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("answer"));
        Assert.Throws<InvalidOperationException>(() => runtime.TryGetFunction("answer", out _));
    }

    [Fact]
    public void Runtime_reports_alive_state_for_successful_execution()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();
        var answer = runtime.GetFunction("answer");

        Assert.True(runtime.IsAlive);
        Assert.False(runtime.TryGetLastError(out var noError));
        Assert.Null(noError);
        Assert.Equal(42, answer.CallInt64());
        Assert.True(runtime.IsAlive);
    }

    [Fact]
    public void Public_runtime_handles_format_diagnostic_strings()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn answer*(): int {
                host::noop()
                return 42
            }
            """);

        runtime.AddModule("host.um", "fn noop*()");
        var callback = runtime.Register("noop", _ => UmkaValue.Void);
        var handle = runtime.CreateHostHandle(new HostBox(42));

        Assert.Equal(UmkaRuntimeState.Created, runtime.State);
        Assert.Equal("UmkaRuntime(Created)", runtime.ToString());
        Assert.Equal("UmkaCallback(noop, Registered)", callback.ToString());
        Assert.Contains("HostBox", handle.ToString());
        Assert.Contains("Alive", handle.ToString());

        runtime.Compile();
        var answer = runtime.GetFunction("answer");

        Assert.Equal(UmkaRuntimeState.Compiled, runtime.State);
        Assert.Equal("UmkaRuntime(Compiled)", runtime.ToString());
        Assert.Contains("answer", answer.ToString());
        Assert.Contains("Parameters=0", answer.ToString());
        Assert.Contains("Result=int", answer.ToString());

        handle.Dispose();

        Assert.Contains("Disposed", handle.ToString());

        runtime.Dispose();

        Assert.Equal(UmkaRuntimeState.Disposed, runtime.State);
        Assert.Equal("UmkaRuntime(Disposed)", runtime.ToString());
        Assert.Equal("UmkaCallback(noop, Disposed)", callback.ToString());
    }

    [Fact]
    public void Runtime_rejects_mutation_after_compile()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        Assert.Throws<InvalidOperationException>(() => runtime.AddModule("late.um", "fn late*(): int"));
        Assert.Throws<InvalidOperationException>(() => runtime.AddModuleFromFile("late.um", "missing-late.um"));
        Assert.Throws<InvalidOperationException>(() => runtime.Register("late", _ => UmkaValue.From(1)));
        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
    }

    [Fact]
    public void Runtime_rejects_recompile_and_mutation_after_compile_failure()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("fn broken*( {", fileName: "broken.um");
        var box = new HostBox(42);
        var handle = runtime.CreateHostHandle(box);
        var pointer = handle.Address;

        Assert.Equal(UmkaRuntimeState.Created, runtime.State);
        Assert.Equal("UmkaRuntime(Created)", runtime.ToString());
        Assert.Throws<UmkaException>(() => runtime.Compile());
        Assert.Equal(UmkaRuntimeState.CompileAttempted, runtime.State);
        Assert.Equal("UmkaRuntime(CompileAttempted)", runtime.ToString());
        Assert.False(runtime.IsAlive);

        Assert.Throws<InvalidOperationException>(() => runtime.AddModule("late.um", "fn late*(): int"));
        Assert.Throws<InvalidOperationException>(() => runtime.AddModuleFromFile("late.um", "missing-late.um"));
        Assert.Throws<InvalidOperationException>(() => runtime.Register("late", _ => UmkaValue.From(1)));
        Assert.Throws<InvalidOperationException>(() => runtime.CreateHostHandle(new HostBox(1)));
        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
        Assert.Throws<InvalidOperationException>(() => runtime.Run());
        Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("broken"));
        Assert.Throws<InvalidOperationException>(() => runtime.TryGetFunction("broken", out _));
        Assert.False(string.IsNullOrWhiteSpace(runtime.GetLastError().Message));
        Assert.True(runtime.TryGetLastError(out var error));
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));

        Assert.Same(box, handle.GetTarget<HostBox>());
        Assert.Same(box, runtime.GetHostObject<HostBox>(pointer));
        Assert.Equal(pointer, handle.ToValue().AsPointer());

        handle.Dispose();

        Assert.True(handle.IsDisposed);
        Assert.False(runtime.TryGetHostObject<HostBox>(pointer, out var stale));
        Assert.Null(stale);
    }

    [Fact]
    public void Runtime_rejects_use_after_dispose()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.False(runtime.IsDisposed);

        runtime.Dispose();

        Assert.True(runtime.IsDisposed);
        runtime.Dispose();
        Assert.True(runtime.IsDisposed);

        Assert.Throws<ObjectDisposedException>(() => runtime.Compile());
        Assert.Throws<ObjectDisposedException>(() => runtime.Run());
        Assert.Throws<ObjectDisposedException>(() => runtime.AddModule("host.um", "fn noop*()"));
        Assert.Throws<ObjectDisposedException>(() => runtime.AddModuleFromFile("host.um", "host.um"));
        Assert.Throws<ObjectDisposedException>(() => runtime.Register("noop", _ => UmkaValue.Void));
        Assert.Throws<ObjectDisposedException>(() => runtime.CreateHostHandle(new HostBox(1)));
        Assert.Throws<ObjectDisposedException>(() => runtime.GetFunction("answer"));
        Assert.Throws<ObjectDisposedException>(() => runtime.TryGetFunction("answer", out _));
        Assert.Throws<ObjectDisposedException>(() => runtime.GetHostObject<HostBox>(IntPtr.Zero));
        Assert.Throws<ObjectDisposedException>(() => runtime.TryGetHostObject<HostBox>(IntPtr.Zero, out _));
        Assert.Throws<ObjectDisposedException>(() => runtime.GetLastError());
        Assert.Throws<ObjectDisposedException>(() => runtime.TryGetLastError(out _));
        Assert.Throws<ObjectDisposedException>(() => runtime.IsAlive);
    }

    [Fact]
    public void Runtime_dispose_does_not_invoke_warning_handler_for_internal_cleanup_compile()
    {
        NativeTestEnvironment.RequireNativeShim();

        var warningCount = 0;
        var runtime = UmkaRuntime.FromSource(
            """
            fn answer*(): int {
                unused := 1
                return 42
            }
            """,
            new UmkaRuntimeOptions
            {
                WarningHandler = _ => warningCount++,
            });

        runtime.Dispose();

        Assert.Equal(0, warningCount);
    }

    [Fact]
    public void Runtime_finalizer_can_collect_undisposed_runtime_with_registered_callback()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtimeReference = CreateUndisposedRuntimeWithRegisteredCallback();

        ForceFullCollection(runtimeReference);

        Assert.False(runtimeReference.IsAlive);
    }

    [Fact]
    public void Runtime_finalizer_can_release_owned_host_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        var references = CreateUndisposedRuntimeWithHostHandle();

        ForceFullCollection(references.Target);

        Assert.False(references.Runtime.IsAlive);
        Assert.False(references.Handle.IsAlive);
        Assert.False(references.Target.IsAlive);
    }

    [Fact]
    public void Runtime_enforces_thread_affinity()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var ex = ThrowsOnNewThread<InvalidOperationException>(() => runtime.Compile());

        Assert.Contains("owning thread", ex.Message);
    }

    [Fact]
    public void Runtime_enforces_thread_affinity_for_function_operations()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }

            fn main() {
            }
            """);

        runtime.Compile();
        var answer = runtime.GetFunction("answer");

        var callEx = ThrowsOnNewThread<InvalidOperationException>(() => answer.CallInt64());
        var runEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.Run());
        var lookupEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.GetFunction("answer"));
        var tryLookupEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.TryGetFunction("answer", out _));

        Assert.Contains("owning thread", callEx.Message);
        Assert.Contains("owning thread", runEx.Message);
        Assert.Contains("owning thread", lookupEx.Message);
        Assert.Contains("owning thread", tryLookupEx.Message);
    }

    [Fact]
    public void Runtime_dispose_can_release_owned_resources_from_non_owner_thread_after_use()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn answer*(): int {
                return host::answer()
            }
            """);

        runtime.AddModule("host.um", "fn answer*(): int");
        var callback = runtime.Register("answer", _ => UmkaValue.From(42));
        var handle = runtime.CreateHostHandle(new HostBox(7));
        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());

        var disposeException = RecordExceptionOnNewThread(runtime.Dispose);

        Assert.Null(disposeException);
        Assert.True(runtime.IsDisposed);
        Assert.True(callback.IsDisposed);
        Assert.True(handle.IsDisposed);
        Assert.Equal("UmkaRuntime(Disposed)", runtime.ToString());
        Assert.Throws<ObjectDisposedException>(() => runtime.GetLastError());
    }

    [Fact]
    public void Function_metadata_remains_available_after_runtime_disposal_but_calls_are_rejected()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn choose*(value: int, enabled: bool): int {
                if enabled {
                    return value
                }

                return 0
            }
            """);

        runtime.Compile();
        var choose = runtime.GetFunction("choose");
        var parameterTypes = choose.ParameterTypes;

        runtime.Dispose();

        Assert.Equal("choose", choose.Name);
        Assert.Null(choose.ModuleName);
        Assert.Equal("choose", choose.QualifiedName);
        Assert.Equal(2, choose.ParameterCount);
        Assert.Same(parameterTypes, choose.ParameterTypes);
        Assert.Equal(UmkaTypeKind.SignedInteger, parameterTypes[0].Kind);
        Assert.Equal(UmkaTypeKind.Boolean, parameterTypes[1].Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, choose.ResultType.Kind);
        Assert.True(choose.CanReadResultAsScalar<int>());
        Assert.False(choose.CanReadResultAsScalar<string>());
        Assert.True(choose.CanReadResultAsValue());
        Assert.True(choose.CanCallWith(UmkaValue.From(42), UmkaValue.From(true)));
        Assert.False(choose.CanCallWith(UmkaValue.From("not-int"), UmkaValue.From(true)));
        Assert.Contains("choose", choose.ToString());
        Assert.Throws<ObjectDisposedException>(() => choose.CallInt64(UmkaValue.From(42), UmkaValue.From(true)));
    }

    [Fact]
    public void Runtime_validates_creation_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => UmkaRuntime.FromSource(null!));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}", ""));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}\0trailing"));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}", "main\0trailing.um"));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromFile(""));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromFile("main\0trailing.um"));
        Assert.Throws<FileNotFoundException>(() => UmkaRuntime.FromFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.um")));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}", arguments: new List<string> { null! }));
        Assert.Throws<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}", arguments: new List<string> { "script.um", "bad\0argument" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => UmkaRuntime.FromSource("fn main() {}", new UmkaRuntimeOptions { StackSize = 0 }));
    }

    [Fact]
    public void Runtime_rejects_embedded_null_in_native_string_inputs()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.Throws<ArgumentException>(() => runtime.AddModule("bad\0module.um", "fn value*(): int"));
        Assert.Throws<ArgumentException>(() => runtime.AddModule("bad.um", "fn value*(): int\0"));
        Assert.Throws<ArgumentException>(() => runtime.Register("bad\0callback", _ => UmkaValue.Void));

        runtime.Compile();

        Assert.Throws<ArgumentException>(() => runtime.GetFunction("answer\0trailing"));
        Assert.Throws<ArgumentException>(() => runtime.TryGetFunction("answer", "module\0trailing.um", out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateUndisposedRuntimeWithRegisteredCallback()
    {
        var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::answer()
            }
            """);

        runtime.AddModule("host.um", "fn answer*(): int");
        runtime.Register("answer", _ => UmkaValue.From(42));
        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("run").CallInt64());

        return new WeakReference(runtime);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Runtime, WeakReference Handle, WeakReference Target) CreateUndisposedRuntimeWithHostHandle()
    {
        var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var target = new HostBox(42);
        var handle = runtime.CreateHostHandle(target);

        Assert.Equal(42, handle.GetTarget<HostBox>().Value);

        return (new WeakReference(runtime), new WeakReference(handle), new WeakReference(target));
    }

    private static void ForceFullCollection(WeakReference reference)
    {
        for (var i = 0; i < 5 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private static TException ThrowsOnNewThread<TException>(Action action)
        where TException : Exception
    {
        return Assert.IsType<TException>(RecordExceptionOnNewThread(action));
    }

    private static Exception? RecordExceptionOnNewThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.Start();
        thread.Join();

        return exception;
    }

    private sealed record HostBox(int Value);
}
