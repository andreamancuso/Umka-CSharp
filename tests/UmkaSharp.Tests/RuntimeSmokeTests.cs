using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeSmokeTests
{
    [Fact]
    public void Runtime_can_call_umka_function_with_primitive_arguments()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn add*(a, b: int): int {
                return a + b
            }
            """);

        runtime.Compile();
        var add = runtime.GetFunction("add");

        Assert.Equal(42, add.CallInt64(UmkaValue.From(19), UmkaValue.From(23)));
    }

    [Fact]
    public void Runtime_can_call_managed_callback_from_umka()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::triple(14)
            }
            """);

        runtime.AddModule("host.um", "fn triple*(x: int): int");
        runtime.Register("triple", frame => UmkaValue.From(frame.GetInt64(0) * 3));

        runtime.Compile();
        var run = runtime.GetFunction("run");

        Assert.Equal(42, run.CallInt64());
    }

    [Fact]
    public void Runtime_can_roundtrip_strings()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn greet*(name: str): str {
                return "Hello, " + name
            }
            """);

        runtime.Compile();
        var greet = runtime.GetFunction("greet");

        Assert.Equal("Hello, Umka", greet.CallString(UmkaValue.From("Umka")));
    }

    [Fact]
    public void Runtime_can_read_static_array_result_as_struct()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn pair*(x, y: real): [2]real {
                return [2]real{x, y}
            }
            """);

        runtime.Compile();
        var pair = runtime.GetFunction("pair");

        var result = pair.CallStruct<RealPair>(UmkaValue.From(2.5), UmkaValue.From(7.5));

        Assert.Equal(2.5, result.X);
        Assert.Equal(7.5, result.Y);
    }

    [Fact]
    public void Runtime_can_marshal_scalar_values_in_both_directions()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn scale*(x: real, enabled: bool, count: uint): real {
                return host::scale(x, enabled, count)
            }

            fn invert*(value: bool): bool {
                return !value
            }

            fn bump*(value: uint): uint {
                return value + uint(2)
            }
            """);

        runtime.AddModule("host.um", "fn scale*(x: real, enabled: bool, count: uint): real");
        runtime.Register("scale", frame =>
        {
            Assert.InRange(frame.GetDouble(0), 2.499, 2.501);
            Assert.True(frame.GetBoolean(1));
            Assert.Equal((ulong)4, frame.GetUInt64(2));
            return UmkaValue.From(frame.GetDouble(0) * frame.GetUInt64(2));
        });

        runtime.Compile();

        var scaled = runtime.GetFunction("scale").CallDouble(
            UmkaValue.From(2.5),
            UmkaValue.From(true),
            UmkaValue.From((ulong)4));
        Assert.InRange(scaled, 9.999, 10.001);

        Assert.False(runtime.GetFunction("invert").CallBoolean(UmkaValue.From(true)));
        Assert.Equal((ulong)44, runtime.GetFunction("bump").CallUInt64(UmkaValue.From((ulong)42)));
    }

    [Fact]
    public void Runtime_surfaces_managed_callback_exceptions_as_umka_errors()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::explode()
            }
            """);

        runtime.AddModule("host.um", "fn explode*(): int");
        var callback = runtime.Register("explode", _ => throw new InvalidOperationException("boom"));

        runtime.Compile();

        var run = runtime.GetFunction("run");
        var ex = Assert.Throws<UmkaException>(() => run.CallInt64());

        Assert.Contains("Managed callback failed", ex.Error.Message);
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Runtime_surfaces_compile_errors()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("fn broken*( {");

        var ex = Assert.Throws<UmkaException>(() => runtime.Compile());
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
    }

    [Fact]
    public void Runtime_rejects_double_compile()
    {
        RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
    }

    private static void RequireNativeShim()
    {
        if (NativeLibrary.TryLoad(
            "umka_shim",
            typeof(UmkaRuntime).Assembly,
            DllImportSearchPath.AssemblyDirectory,
            out var handle))
        {
            NativeLibrary.Free(handle);
            return;
        }

        throw new InvalidOperationException(
            "umka_shim could not be loaded. Build the native shim before running tests.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealPair
    {
        public double X;
        public double Y;
    }
}
