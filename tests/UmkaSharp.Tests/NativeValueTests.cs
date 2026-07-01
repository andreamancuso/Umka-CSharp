using Xunit;

namespace UmkaSharp.Tests;

public sealed class NativeValueTests
{
    [Fact]
    public void Function_can_retain_string_result_and_pass_it_back()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeText*(): str {
                return "umka"
            }

            fn textLen*(value: str): int {
                return len(value)
            }
            """);

        runtime.Compile();

        var makeText = runtime.GetFunction("makeText");
        var textLen = runtime.GetFunction("textLen");

        Assert.True(makeText.CanReadResultAsNativeValue());
        using var text = makeText.CallNativeValue();

        Assert.Equal(UmkaTypeKind.String, text.Type.Kind);
        Assert.False(text.IsDisposed);
        Assert.True(textLen.CanCallWith(text.ToValue()));
        Assert.Equal(4, textLen.CallInt64(text.ToValue()));
        Assert.False(textLen.TryCallNativeValue(out var retainedLength, text.ToValue()));
        Assert.Null(retainedLength);
    }

    [Fact]
    public void Function_can_retain_map_result_and_pass_it_back()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn scores*(): map[str]int {
                return map[str]int{"alpha": 17, "beta": 25}
            }

            fn total*(value: map[str]int): int {
                return value["alpha"] + value["beta"]
            }
            """);

        runtime.Compile();

        var scores = runtime.GetFunction("scores");
        var total = runtime.GetFunction("total");

        Assert.True(scores.CanReadResultAsNativeValue());
        using var retained = scores.CallNativeValue();

        Assert.Equal(UmkaTypeKind.Map, retained.Type.Kind);
        Assert.True(total.CanCallWith(retained.ToValue()));
        Assert.Equal(42, total.CallInt64(retained.ToValue()));
    }

    [Fact]
    public void Retained_concrete_struct_can_be_passed_to_interface_parameter()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            type Speaker = interface {
                speak(): str
            }

            type Dog = struct {
                woofCount: int
            }

            fn (dog: ^Dog) speak(): str {
                dog.woofCount++
                return "woof"
            }

            fn dog*(): Dog {
                return Dog{woofCount: 0}
            }

            fn speak*(speaker: Speaker): str {
                return speaker.speak()
            }
            """);

        using var dog = runtime.GetFunction("dog").CallNativeValue();
        var speak = runtime.GetFunction("speak");

        Assert.Equal(UmkaTypeKind.Struct, dog.Type.Kind);
        Assert.Equal(UmkaTypeKind.Interface, speak.ParameterTypes[0].Kind);
        Assert.False(speak.ParameterTypes[0].IsAny);
        Assert.True(speak.CanCallWith(dog.ToValue()));
        Assert.Equal("woof", speak.CallString(dog.ToValue()));
    }

    [Fact]
    public void Callback_can_return_retained_concrete_struct_to_interface_result()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            type Dog = struct {
                woofCount: int
            }

            fn (dog: ^Dog) speak(): str {
                dog.woofCount++
                return "woof"
            }

            fn dog*(): Dog {
                return Dog{woofCount: 0}
            }

            fn useProduced*(): str {
                return host::produce().speak()
            }
            """);

        runtime.AddModule("host.um", """
            type Speaker* = interface {
                speak(): str
            }

            fn produce*(): Speaker
            """);

        UmkaNativeValue? retained = null;
        runtime.Register("produce", frame =>
        {
            Assert.NotNull(retained);
            var result = retained.ToValue();
            Assert.True(frame.CanReturn(result));
            return result;
        });

        runtime.Compile();
        retained = runtime.GetFunction("dog").CallNativeValue();

        using (retained)
        {
            Assert.Equal("woof", runtime.GetFunction("useProduced").CallString());
        }
    }

    [Fact]
    public void Callback_can_retain_argument_after_frame_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn captureText*() {
                host::capture("persist")
            }

            fn textLen*(value: str): int {
                return len(value)
            }
            """);

        runtime.AddModule("host.um", "fn capture*(value: str)");

        UmkaNativeValue? retained = null;
        runtime.Register("capture", frame =>
        {
            Assert.True(frame.CanReadArgumentAsNativeValue(0));
            Assert.True(frame.TryGetNativeValue(0, out var transient));
            transient.Dispose();

            retained = frame.GetNativeValue(0);
            return UmkaValue.Void;
        });

        runtime.Compile();
        runtime.GetFunction("captureText").CallVoid();

        Assert.NotNull(retained);
        using var value = retained;
        Assert.Equal(7, runtime.GetFunction("textLen").CallInt64(value.ToValue()));
    }

    [Fact]
    public void Callback_can_return_retained_native_value()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn makeText*(): str {
                return "callback"
            }

            fn useProduced*(): int {
                return len(host::produce())
            }
            """);

        runtime.AddModule("host.um", "fn produce*(): str");

        UmkaNativeValue? retained = null;
        runtime.Register("produce", frame =>
        {
            Assert.NotNull(retained);
            var result = retained.ToValue();
            Assert.True(frame.CanReturn(result));
            return result;
        });

        runtime.Compile();
        retained = runtime.GetFunction("makeText").CallNativeValue();

        using (retained)
        {
            Assert.Equal(8, runtime.GetFunction("useProduced").CallInt64());
        }
    }

    [Fact]
    public void Native_value_disposal_rejects_later_use()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeText*(): str {
                return "done"
            }
            """);

        runtime.Compile();
        var value = runtime.GetFunction("makeText").CallNativeValue();

        value.Dispose();

        Assert.True(value.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => value.ToValue());
        Assert.Throws<ObjectDisposedException>(() => UmkaValue.FromNativeValue(value));
    }

    [Fact]
    public void Native_value_from_foreign_runtime_is_rejected()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var sourceRuntime = UmkaRuntime.FromSource("""
            fn makeText*(): str {
                return "foreign"
            }
            """);
        sourceRuntime.Compile();
        using var retained = sourceRuntime.GetFunction("makeText").CallNativeValue();

        using var targetRuntime = UmkaRuntime.FromSource("""
            fn textLen*(value: str): int {
                return len(value)
            }
            """);
        targetRuntime.Compile();
        var textLen = targetRuntime.GetFunction("textLen");

        Assert.False(textLen.CanCallWith(retained.ToValue()));
        Assert.Throws<InvalidOperationException>(() => textLen.CallInt64(retained.ToValue()));
    }

    [Fact]
    public void Native_values_can_be_disposed_after_runtime_error_and_runtime_disposal()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn makeText*(): str {
                return "ok"
            }

            fn fail*(): int {
                value := 0
                return 1 / value
            }
            """);

        runtime.Compile();
        var retained = runtime.GetFunction("makeText").CallNativeValue();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("fail").CallInt64());
        retained.Dispose();
        Assert.True(retained.IsDisposed);

        var runtimeForDisposal = UmkaRuntime.FromSource("""
            fn makeText*(): str {
                return "runtime"
            }
            """);
        runtimeForDisposal.Compile();
        var retainedForRuntimeDisposal = runtimeForDisposal.GetFunction("makeText").CallNativeValue();

        runtimeForDisposal.Dispose();

        Assert.True(retainedForRuntimeDisposal.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => retainedForRuntimeDisposal.ToValue());
    }
}
