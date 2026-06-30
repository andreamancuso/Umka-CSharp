using Xunit;

namespace UmkaSharp.Tests;

public sealed class HostHandleTests
{
    [Fact]
    public void Runtime_can_pass_host_handles_through_umka_pointer_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        runtime.Register("read", frame =>
        {
            var box = frame.GetHostObject<HostBox>(0);
            return UmkaValue.From(box.Value);
        });

        using var handle = runtime.CreateHostHandle(new HostBox(42));

        Assert.False(handle.IsDisposed);
        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("read").CallInt64(UmkaValue.FromHostHandle(handle)));
        Assert.Equal(42, handle.GetTarget<HostBox>().Value);
        Assert.Equal(handle.Address, handle.ToValue().AsPointer());
    }

    [Fact]
    public void Runtime_can_resolve_host_handles_returned_from_umka_pointer_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn echo*(handle: ^void): ^void {
                return handle
            }
            """);

        var box = new HostBox(42);
        using var handle = runtime.CreateHostHandle(box);

        runtime.Compile();

        var pointer = runtime.GetFunction("echo").CallPointer(UmkaValue.FromHostHandle(handle));

        Assert.Equal(handle.Address, pointer);
        Assert.Same(box, runtime.GetHostObject<HostBox>(pointer));
        Assert.Same(box, runtime.GetFunction("echo").CallHostObject<HostBox>(UmkaValue.FromHostHandle(handle)));
        Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("echo").CallHostObject<HostBox>(UmkaValue.FromPointer(IntPtr.Zero)));
        Assert.Throws<InvalidCastException>(() => runtime.GetFunction("echo").CallHostObject<string>(UmkaValue.FromHostHandle(handle)));
    }

    [Fact]
    public void Runtime_can_try_resolve_host_handles_returned_from_umka_pointer_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn echo*(handle: ^void): ^void {
                return handle
            }
            """);

        var box = new HostBox(42);
        using var handle = runtime.CreateHostHandle(box);

        runtime.Compile();

        var echo = runtime.GetFunction("echo");
        var handleValue = UmkaValue.FromHostHandle(handle);
        var arguments = new[] { handleValue };

        Assert.True(echo.TryCallHostObject<HostBox>(out var target, handleValue));
        Assert.Same(box, target);

        Assert.True(echo.TryCallHostObject<HostBox>(arguments.AsSpan(), out var spanTarget));
        Assert.Same(box, spanTarget);

        Assert.False(echo.TryCallHostObject<HostBox>(out var nullTarget, UmkaValue.FromPointer(IntPtr.Zero)));
        Assert.Null(nullTarget);

        Assert.False(echo.TryCallHostObject<string>(out var wrongTypeTarget, handleValue));
        Assert.Null(wrongTypeTarget);

        var stalePointer = handle.Address;
        handle.Dispose();

        Assert.False(echo.TryCallHostObject<HostBox>(out var staleTarget, UmkaValue.FromPointer(stalePointer)));
        Assert.Null(staleTarget);
    }

    [Fact]
    public void Try_call_host_object_preserves_function_execution_failures()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn echo*(handle: ^void): ^void {
                return handle
            }

            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        using var handle = runtime.CreateHostHandle(new HostBox(42));

        runtime.Compile();
        var echo = runtime.GetFunction("echo");
        var divide = runtime.GetFunction("divide");
        var handleValue = UmkaValue.FromHostHandle(handle);

        var threadEx = ThrowsOnNewThread<InvalidOperationException>(() => echo.TryCallHostObject<HostBox>(out _, handleValue));
        Assert.Contains("owning thread", threadEx.Message);

        Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        var terminatedEx = Assert.Throws<InvalidOperationException>(() => echo.TryCallHostObject<HostBox>(out _, handleValue));
        Assert.Contains("terminated", terminatedEx.Message);

        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(() => echo.TryCallHostObject<HostBox>(out _, handleValue));
    }

    [Fact]
    public void Runtime_can_try_resolve_host_handle_pointers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var box = new HostBox(42);
        var handle = runtime.CreateHostHandle(box);
        var pointer = handle.Address;

        Assert.True(runtime.TryGetHostObject<HostBox>(pointer, out var resolved));
        Assert.Same(box, resolved);

        Assert.False(runtime.TryGetHostObject<HostBox>(IntPtr.Zero, out var nullResolved));
        Assert.Null(nullResolved);

        Assert.False(runtime.TryGetHostObject<HostBox>(new IntPtr(12345), out var unknownResolved));
        Assert.Null(unknownResolved);

        Assert.False(runtime.TryGetHostObject<string>(pointer, out var wrongTypeResolved));
        Assert.Null(wrongTypeResolved);

        handle.Dispose();

        Assert.False(runtime.TryGetHostObject<HostBox>(pointer, out var staleResolved));
        Assert.Null(staleResolved);
    }

    [Fact]
    public void Callback_frame_can_try_resolve_host_handle_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle, maybe: ^void): int {
                return host::read(handle, maybe)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle, maybe: ^void): int");
        var sawCallback = false;
        runtime.Register("read", frame =>
        {
            Assert.True(frame.TryGetHostObject<HostBox>(0, out var box));
            var resolved = box ?? throw new InvalidOperationException("Expected host handle resolution to succeed.");
            Assert.Equal(42, resolved.Value);

            Assert.False(frame.TryGetHostObject<HostBox>(1, out var missing));
            Assert.Null(missing);

            Assert.False(frame.TryGetHostObject<string>(0, out var wrongType));
            Assert.Null(wrongType);

            sawCallback = true;
            return UmkaValue.From(resolved.Value);
        });

        using var handle = runtime.CreateHostHandle(new HostBox(42));

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("read").CallInt64(handle.ToValue(), UmkaValue.FromPointer(IntPtr.Zero)));
        Assert.True(sawCallback);
    }

    [Fact]
    public void Callback_frame_rejects_host_handle_resolution_after_callback_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        UmkaCallFrame capturedFrame = default;
        runtime.Register("read", frame =>
        {
            capturedFrame = frame;
            Assert.True(frame.TryGetHostObject<HostBox>(0, out var box));
            return UmkaValue.From(box?.Value ?? 0);
        });

        using var handle = runtime.CreateHostHandle(new HostBox(42));

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("read").CallInt64(handle.ToValue()));

        var strictEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.GetHostObject<HostBox>(0));
        var tryEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.TryGetHostObject<HostBox>(0, out _));

        Assert.Contains("no longer active", strictEx.Message);
        Assert.Contains("no longer active", tryEx.Message);
    }

    [Fact]
    public void Runtime_rejects_unknown_host_handle_pointers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        var callback = runtime.Register("read", frame =>
        {
            _ = frame.GetHostObject<HostBox>(0);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<InvalidOperationException>(() => runtime.GetHostObject<HostBox>(IntPtr.Zero));
        Assert.Throws<InvalidOperationException>(() => runtime.GetHostObject<HostBox>(new IntPtr(12345)));
        Assert.Throws<UmkaException>(() => runtime.GetFunction("read").CallInt64(UmkaValue.FromPointer(new IntPtr(12345))));
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Runtime_rejects_disposed_host_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        var callback = runtime.Register("read", frame =>
        {
            _ = frame.GetHostObject<HostBox>(0);
            return UmkaValue.From(0);
        });

        var handle = runtime.CreateHostHandle(new HostBox(42));
        var pointer = handle.Address;
        Assert.False(handle.IsDisposed);

        handle.Dispose();

        Assert.True(handle.IsDisposed);
        Assert.Throws<InvalidOperationException>(() => runtime.GetHostObject<HostBox>(pointer));
        runtime.Compile();

        Assert.Throws<ObjectDisposedException>(() => UmkaValue.FromHostHandle(handle));
        Assert.Throws<UmkaException>(() => runtime.GetFunction("read").CallInt64(UmkaValue.FromPointer(pointer)));
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Runtime_rejects_host_handle_type_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        var callback = runtime.Register("read", frame =>
        {
            _ = frame.GetHostObject<string>(0);
            return UmkaValue.From(0);
        });

        using var handle = runtime.CreateHostHandle(new HostBox(42));

        runtime.Compile();

        Assert.Throws<InvalidCastException>(() => runtime.GetHostObject<string>(handle.Address));
        Assert.Throws<UmkaException>(() => runtime.GetFunction("read").CallInt64(handle.ToValue()));
        Assert.IsType<InvalidCastException>(callback.LastException);
    }

    [Fact]
    public void Runtime_rejects_host_handles_from_other_runtimes()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var owner = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);
        using var foreignHandle = owner.CreateHostHandle(new HostBox(42));

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        var callback = runtime.Register("read", frame =>
        {
            _ = frame.GetHostObject<HostBox>(0);
            return UmkaValue.From(0);
        });

        Assert.Equal(42, owner.GetHostObject<HostBox>(foreignHandle.Address).Value);
        Assert.False(runtime.TryGetHostObject<HostBox>(foreignHandle.Address, out var missing));
        Assert.Null(missing);
        Assert.Throws<InvalidOperationException>(() => runtime.GetHostObject<HostBox>(foreignHandle.Address));

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("read").CallInt64(UmkaValue.FromHostHandle(foreignHandle)));
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Runtime_disposes_owned_host_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var handle = runtime.CreateHostHandle(new HostBox(42));

        runtime.Dispose();

        Assert.True(handle.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => handle.Address);
        Assert.Throws<ObjectDisposedException>(() => handle.Target);
        Assert.Throws<ObjectDisposedException>(() => runtime.CreateHostHandle(new HostBox(1)));
    }

    [Fact]
    public void Runtime_rejects_new_host_handles_after_termination_but_existing_handles_remain_owned()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        var box = new HostBox(42);
        var handle = runtime.CreateHostHandle(box);
        var pointer = handle.Address;

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        Assert.False(runtime.IsAlive);
        Assert.Throws<InvalidOperationException>(() => runtime.CreateHostHandle(new HostBox(1)));
        Assert.Same(box, runtime.GetHostObject<HostBox>(pointer));
        Assert.True(runtime.TryGetHostObject<HostBox>(pointer, out var resolved));
        Assert.Same(box, resolved);
        Assert.Equal(pointer, handle.ToValue().AsPointer());

        handle.Dispose();

        Assert.True(handle.IsDisposed);
        Assert.False(runtime.TryGetHostObject<HostBox>(pointer, out var stale));
        Assert.Null(stale);
    }

    [Fact]
    public void Host_handle_reads_target_with_strict_and_try_style_helpers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var handle = runtime.CreateHostHandle(new HostBox(42));

        Assert.Equal(42, handle.GetTarget<HostBox>().Value);
        Assert.True(handle.TryGetTarget<HostBox>(out var target));
        Assert.Equal(42, target.Value);
        Assert.Throws<InvalidCastException>(() => handle.GetTarget<string>());
        Assert.False(handle.TryGetTarget<string>(out var wrongTypeTarget));
        Assert.Null(wrongTypeTarget);

        handle.Dispose();

        Assert.True(handle.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => handle.GetTarget<HostBox>());
        Assert.Throws<ObjectDisposedException>(() => handle.TryGetTarget<HostBox>(out _));
        Assert.Throws<ObjectDisposedException>(() => handle.ToValue());
    }

    [Fact]
    public void Runtime_enforces_thread_affinity_for_host_handle_lifecycle()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        using var handle = runtime.CreateHostHandle(new HostBox(42));
        var pointer = handle.Address;

        var createEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.CreateHostHandle(new HostBox(1)));
        var resolveEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.GetHostObject<HostBox>(pointer));
        var tryResolveEx = ThrowsOnNewThread<InvalidOperationException>(() => runtime.TryGetHostObject<HostBox>(pointer, out _));
        var addressEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = handle.Address);
        var targetEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = handle.Target);
        var typedTargetEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = handle.GetTarget<HostBox>());
        var tryTypedTargetEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = handle.TryGetTarget<HostBox>(out _));
        var valueEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = handle.ToValue());
        var fromHandleEx = ThrowsOnNewThread<InvalidOperationException>(() => _ = UmkaValue.FromHostHandle(handle));
        var disposeEx = ThrowsOnNewThread<InvalidOperationException>(() => handle.Dispose());

        Assert.Contains("owning thread", createEx.Message);
        Assert.Contains("owning thread", resolveEx.Message);
        Assert.Contains("owning thread", tryResolveEx.Message);
        Assert.Contains("owning thread", addressEx.Message);
        Assert.Contains("owning thread", targetEx.Message);
        Assert.Contains("owning thread", typedTargetEx.Message);
        Assert.Contains("owning thread", tryTypedTargetEx.Message);
        Assert.Contains("owning thread", valueEx.Message);
        Assert.Contains("owning thread", fromHandleEx.Message);
        Assert.Contains("owning thread", disposeEx.Message);
        Assert.Equal(42, handle.GetTarget<HostBox>().Value);
    }

    private static TException ThrowsOnNewThread<TException>(Action action)
        where TException : Exception
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

        return Assert.IsType<TException>(exception);
    }

    private sealed record HostBox(int Value);
}
