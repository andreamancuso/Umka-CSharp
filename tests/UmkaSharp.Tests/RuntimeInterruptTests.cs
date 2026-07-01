using System.Runtime.ExceptionServices;
using System.Threading;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeInterruptTests
{
    [Fact]
    public void Runtime_can_clear_pending_interrupt_before_call()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.False(runtime.IsInterruptRequested);

        runtime.RequestInterrupt("clear me");
        Assert.True(runtime.IsInterruptRequested);

        runtime.ClearInterrupt();
        Assert.False(runtime.IsInterruptRequested);

        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        Assert.True(runtime.IsAlive);
    }

    [Fact]
    public void Runtime_interrupt_before_call_stops_umka_execution()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            fn spin*(): int {
                var i: int
                for true {
                    i++
                }
                return i
            }
            """);

        runtime.RequestInterrupt("stop before call");

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("spin").CallInt64());

        Assert.Contains("stop before call", ex.Error.Message, StringComparison.Ordinal);
        Assert.False(runtime.IsAlive);
        Assert.True(runtime.IsInterruptRequested);

        runtime.ClearInterrupt();
        Assert.False(runtime.IsInterruptRequested);
    }

    [Fact]
    public void Callback_can_request_runtime_interrupt()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn callbackInterrupt*(): int {
                host::requestInterrupt()
                var i: int
                for true {
                    i++
                }
                return i
            }
            """);

        runtime.AddModule("host.um", "fn requestInterrupt*()");
        runtime.Register("requestInterrupt", _ =>
        {
            runtime.RequestInterrupt("callback interrupt");
            return UmkaValue.Void;
        });
        runtime.Compile();

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("callbackInterrupt").CallInt64());

        Assert.Contains("callback interrupt", ex.Error.Message, StringComparison.Ordinal);
        Assert.False(runtime.IsAlive);
        Assert.True(runtime.IsInterruptRequested);
    }

    [Fact]
    public void RequestInterrupt_can_be_called_from_another_thread()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Exception? workerException = null;
        var worker = new Thread(() =>
        {
            try
            {
                runtime.RequestInterrupt("worker interrupt");
            }
            catch (Exception ex)
            {
                workerException = ex;
            }
        });

        worker.Start();
        worker.Join();
        if (workerException is not null)
            ExceptionDispatchInfo.Capture(workerException).Throw();

        Assert.True(runtime.IsInterruptRequested);
        runtime.ClearInterrupt();
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
    }
}
