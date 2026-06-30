using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeStressTests
{
    [Fact]
    public void Runtime_can_create_compile_call_and_dispose_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        for (var i = 0; i < 50; i++)
        {
            using var runtime = UmkaRuntime.FromSource("""
                fn answer*(): int {
                    return 42
                }
                """);

            runtime.Compile();

            Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        }
    }

    [Fact]
    public void Runtime_can_call_managed_callback_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        var callCount = 0;
        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inc(41)
            }
            """);

        runtime.AddModule("host.um", "fn inc*(x: int): int");
        runtime.Register("inc", frame =>
        {
            callCount++;
            return UmkaValue.From(frame.GetInt64(0) + 1);
        });

        runtime.Compile();
        var run = runtime.GetFunction("run");

        for (var i = 0; i < 1_000; i++)
            Assert.Equal(42, run.CallInt64());

        Assert.Equal(1_000, callCount);
    }

    [Fact]
    public void Runtime_can_register_invoke_and_dispose_callbacks_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        for (var i = 0; i < 100; i++)
        {
            var runtime = UmkaRuntime.FromSource("""
                import "host.um"

                fn run*(value: int): int {
                    return host::inc(value) + host::twice(value)
                }
                """);

            runtime.AddModule("host.um", """
                fn inc*(value: int): int
                fn twice*(value: int): int
                """);

            var incCalls = 0;
            var twiceCalls = 0;
            var inc = runtime.Register("inc", frame =>
            {
                incCalls++;
                return UmkaValue.From(frame.GetInt64(0) + 1);
            });
            var twice = runtime.Register("twice", frame =>
            {
                twiceCalls++;
                return UmkaValue.From(frame.GetInt64(0) * 2);
            });

            runtime.Compile();
            var run = runtime.GetFunction("run");

            Assert.Equal(i + 1 + (i * 2), run.CallInt64(UmkaValue.From(i)));
            Assert.Equal(1, incCalls);
            Assert.Equal(1, twiceCalls);
            Assert.False(inc.IsDisposed);
            Assert.False(twice.IsDisposed);

            runtime.Dispose();

            Assert.True(inc.IsDisposed);
            Assert.True(twice.IsDisposed);
            runtime.Dispose();
        }
    }

    [Fact]
    public void Runtime_can_run_main_entry_point_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        var callCount = 0;
        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn main() {
                host::tick()
            }
            """);

        runtime.AddModule("host.um", "fn tick*()");
        runtime.Register("tick", _ =>
        {
            callCount++;
            return UmkaValue.Void;
        });

        runtime.Compile();

        for (var i = 0; i < 1_000; i++)
            runtime.Run();

        Assert.Equal(1_000, callCount);
    }

    [Fact]
    public void Runtime_can_roundtrip_strings_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn echo*(value: str): str {
                return value
            }
            """);

        runtime.Compile();
        var echo = runtime.GetFunction("echo");

        for (var i = 0; i < 2_000; i++)
        {
            var value = i % 17 == 0 ? null : $"zażółć-{i}";
            Assert.Equal(value, echo.CallString(UmkaValue.From(value)));
        }
    }

    [Fact]
    public void Runtime_can_read_structured_results_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn pair*(x, y: real): [2]real {
                return [2]real{x, y}
            }
            """);

        runtime.Compile();
        var pair = runtime.GetFunction("pair");
        var arguments = new[] { UmkaValue.From(0.0), UmkaValue.From(0.0) };

        for (var i = 0; i < 1_000; i++)
        {
            var x = i + 0.25;
            var y = i + 0.75;
            arguments[0] = UmkaValue.From(x);
            arguments[1] = UmkaValue.From(y);

            var result = pair.CallStruct<RealPair>(arguments);

            Assert.Equal(x, result.X);
            Assert.Equal(y, result.Y);
        }
    }

    [Fact]
    public void Runtime_can_read_structured_results_after_repeated_reader_validation_failures()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            reads := 0

            fn pair*(x, y: real): [2]real {
                reads++
                return [2]real{x, y}
            }

            fn readCount*(): int {
                return reads
            }
            """);

        runtime.Compile();
        var pair = runtime.GetFunction("pair");
        var readCount = runtime.GetFunction("readCount");
        var arguments = new[] { UmkaValue.From(0.0), UmkaValue.From(0.0) };

        for (var i = 0; i < 500; i++)
        {
            var x = i + 0.25;
            var y = i + 0.75;
            arguments[0] = UmkaValue.From(x);
            arguments[1] = UmkaValue.From(y);

            Assert.Throws<InvalidOperationException>(() => pair.CallArray<double>(1, arguments));
            Assert.Throws<InvalidOperationException>(() => pair.CallStruct<RealTriple>(arguments));
            Assert.Equal(i, readCount.CallInt64());

            var result = pair.CallStruct<RealPair>(arguments);

            Assert.Equal(x, result.X);
            Assert.Equal(y, result.Y);
            Assert.Equal(i + 1, readCount.CallInt64());
        }
    }

    [Fact]
    public void Runtime_can_read_structured_results_after_repeated_argument_validation_failures()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            reads := 0

            fn pair*(x, y: real): [2]real {
                reads++
                return [2]real{x, y}
            }

            fn readCount*(): int {
                return reads
            }
            """);

        runtime.Compile();
        var pair = runtime.GetFunction("pair");
        var readCount = runtime.GetFunction("readCount");
        var validArguments = new[] { UmkaValue.From(0.0), UmkaValue.From(0.0) };
        var invalidArguments = new[] { UmkaValue.From("not-real"), UmkaValue.From(0.0) };

        for (var i = 0; i < 500; i++)
        {
            Assert.Throws<ArgumentException>(() => pair.CallStruct<RealPair>(invalidArguments));
            Assert.Throws<ArgumentException>(() => pair.CallArray<double>(2, invalidArguments));
            Assert.Equal(i, readCount.CallInt64());
            Assert.True(runtime.IsAlive);

            var x = i + 0.25;
            var y = i + 0.75;
            validArguments[0] = UmkaValue.From(x);
            validArguments[1] = UmkaValue.From(y);

            var result = pair.CallStruct<RealPair>(validArguments);

            Assert.Equal(x, result.X);
            Assert.Equal(y, result.Y);
            Assert.Equal(i + 1, readCount.CallInt64());
        }
    }

    [Fact]
    public void Runtime_can_create_use_and_dispose_host_handles_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        var callCount = 0;
        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn read*(handle: ^void): int {
                return host::read(handle)
            }
            """);

        runtime.AddModule("host.um", "fn read*(handle: ^void): int");
        runtime.Register("read", frame =>
        {
            callCount++;
            return UmkaValue.From(frame.GetHostObject<HostBox>(0).Value);
        });

        runtime.Compile();
        var read = runtime.GetFunction("read");

        for (var i = 0; i < 1_000; i++)
        {
            using var handle = runtime.CreateHostHandle(new HostBox(i));

            Assert.Equal(i, read.CallInt64(handle.ToValue()));
            Assert.Equal(i, handle.GetTarget<HostBox>().Value);
        }

        Assert.Equal(1_000, callCount);
    }

    [Fact]
    public void Runtime_can_resolve_host_handle_pointer_results_after_repeated_try_failures()
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
        var handleValue = handle.ToValue();

        for (var i = 0; i < 500; i++)
        {
            Assert.False(echo.TryCallHostObject<HostBox>(out var nullTarget, UmkaValue.FromPointer(IntPtr.Zero)));
            Assert.Null(nullTarget);

            Assert.False(echo.TryCallHostObject<string>(out var wrongTypeTarget, handleValue));
            Assert.Null(wrongTypeTarget);

            var staleHandle = runtime.CreateHostHandle(new HostBox(-i));
            var stalePointer = staleHandle.Address;
            staleHandle.Dispose();

            Assert.False(echo.TryCallHostObject<HostBox>(out var staleTarget, UmkaValue.FromPointer(stalePointer)));
            Assert.Null(staleTarget);

            Assert.True(echo.TryCallHostObject<HostBox>(out var resolved, handleValue));
            Assert.Same(box, resolved);
            Assert.True(runtime.IsAlive);
        }
    }

    [Fact]
    public void Runtime_can_mix_host_handle_callbacks_pointer_results_and_disposal_repeatedly()
    {
        NativeTestEnvironment.RequireNativeShim();

        var callbackCount = 0;
        using var runtime = UmkaRuntime.CompileSource(
            """
            import "host.um"

            fn choose*(first, second: ^void, useSecond: bool): ^void {
                selected := host::choose(first, second, useSecond)
                host::touch(selected)
                return selected
            }
            """,
            configure: configured =>
            {
                configured.AddModule("host.um", """
                    fn choose*(first, second: ^void, useSecond: bool): ^void
                    fn touch*(handle: ^void)
                    """);

                configured.Register("choose", frame =>
                    UmkaValue.FromPointer(frame.GetBoolean(2)
                        ? frame.GetPointer(1)
                        : frame.GetPointer(0)));
                configured.RegisterVoid("touch", frame =>
                {
                    frame.GetHostObject<MutableHostBox>(0).Touches++;
                    callbackCount++;
                });
            });

        var choose = runtime.GetFunction("choose");

        for (var i = 0; i < 500; i++)
        {
            using var first = runtime.CreateHostHandle(new MutableHostBox(i));
            using var second = runtime.CreateHostHandle(new MutableHostBox(i + 1_000));
            var firstTarget = first.GetTarget<MutableHostBox>();
            var secondTarget = second.GetTarget<MutableHostBox>();
            var useSecond = i % 2 == 0;
            var expected = useSecond ? secondTarget : firstTarget;
            var unexpected = useSecond ? firstTarget : secondTarget;
            var arguments = new[] { first.ToValue(), second.ToValue(), UmkaValue.From(useSecond) };

            var selectedPointer = choose.CallPointer(arguments);

            Assert.True(runtime.TryGetHostObject<MutableHostBox>(selectedPointer, out var selected));
            Assert.Same(expected, selected);
            Assert.Equal(1, expected.Touches);
            Assert.Equal(0, unexpected.Touches);

            Assert.Same(expected, choose.CallHostObject<MutableHostBox>(arguments));
            Assert.True(choose.TryCallHostObject<MutableHostBox>(out var trySelected, arguments));
            Assert.Same(expected, trySelected);
            Assert.Equal(3, expected.Touches);
            Assert.Equal(0, unexpected.Touches);

            var secondPointer = second.Address;
            second.Dispose();

            Assert.False(runtime.TryGetHostObject<MutableHostBox>(secondPointer, out var stale));
            Assert.Null(stale);
            Assert.True(runtime.IsAlive);
        }

        Assert.Equal(1_500, callbackCount);
    }

    [Fact]
    public void Runtime_reports_repeated_compile_failures_without_process_failure()
    {
        NativeTestEnvironment.RequireNativeShim();

        for (var i = 0; i < 100; i++)
        {
            using var runtime = UmkaRuntime.FromSource("fn broken*( {", fileName: $"broken-{i}.um");

            var ex = Assert.Throws<UmkaException>(() => runtime.Compile());

            Assert.EndsWith($"broken-{i}.um", ex.Error.FileName);
            Assert.True(ex.Error.Line > 0);
            Assert.True(ex.Error.Position >= 0);
            Assert.NotEqual(0, ex.Error.Code);
            Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
            Assert.False(string.IsNullOrWhiteSpace(runtime.GetLastError().Message));
            Assert.Throws<InvalidOperationException>(() => runtime.Compile());
        }
    }

    [Fact]
    public void Runtime_reports_repeated_umka_runtime_failures_without_process_failure()
    {
        NativeTestEnvironment.RequireNativeShim();

        for (var i = 0; i < 100; i++)
        {
            using var runtime = UmkaRuntime.FromSource("""
                fn divide*(value: int): int {
                    return value / 0
                }
                """, fileName: $"runtime-failure-{i}.um");

            runtime.Compile();
            var divide = runtime.GetFunction("divide");

            var ex = Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(i + 1)));

            Assert.EndsWith($"runtime-failure-{i}.um", ex.Error.FileName);
            Assert.Equal("divide", ex.Error.FunctionName);
            Assert.True(ex.Error.Line > 0);
            Assert.True(ex.Error.Position >= 0);
            Assert.NotEqual(0, ex.Error.Code);
            Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
            Assert.False(runtime.IsAlive);
            Assert.False(string.IsNullOrWhiteSpace(runtime.GetLastError().Message));
            Assert.Throws<InvalidOperationException>(() => divide.CallInt64(UmkaValue.From(1)));
            Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("divide"));
        }
    }

    [Fact]
    public void Runtime_reports_repeated_managed_callback_failures_without_process_failure()
    {
        NativeTestEnvironment.RequireNativeShim();

        for (var i = 0; i < 100; i++)
        {
            using var runtime = UmkaRuntime.FromSource("""
                import "host.um"

                fn run*(): int {
                    return host::fail()
                }
                """);

            runtime.AddModule("host.um", "fn fail*(): int");
            var callback = runtime.Register("fail", _ => throw new InvalidOperationException($"boom-{i}"));

            runtime.Compile();
            var run = runtime.GetFunction("run");

            var ex = Assert.Throws<UmkaException>(() => run.CallInt64());

            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            var callbackEx = Assert.IsType<InvalidOperationException>(callback.LastException);
            Assert.Equal($"boom-{i}", callbackEx.Message);
            Assert.False(runtime.IsAlive);
            Assert.Throws<InvalidOperationException>(() => run.CallInt64());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealPair
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealTriple
    {
        public double X;
        public double Y;
        public double Z;
    }

    private sealed record HostBox(int Value);

    private sealed class MutableHostBox
    {
        public MutableHostBox(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public int Touches { get; set; }
    }
}
