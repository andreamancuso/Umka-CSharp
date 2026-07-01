using Xunit;

namespace UmkaSharp.Tests;

public sealed class CallableTests
{
    [Fact]
    public void Retained_closure_can_be_invoked_from_csharp()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeAdder*(base: int): fn (x: int): int {
                return fn (x: int): int |base| {
                    return base + x
                }
            }
            """);

        runtime.Compile();

        using var retained = runtime.GetFunction("makeAdder").CallNativeValue(UmkaValue.From(10));
        Assert.True(retained.IsCallable);
        Assert.Equal(UmkaTypeKind.Closure, retained.Type.Kind);
        Assert.True(retained.Type.IsCallable);
        Assert.False(retained.Type.IsDeferred);

        var callable = retained.AsCallable();
        Assert.True(callable.IsRetainedCallable);
        Assert.Equal("<callable>", callable.Name);
        Assert.Equal(1, callable.ParameterCount);
        Assert.Equal(UmkaTypeKind.SignedInteger, callable.ParameterTypes[0].Kind);
        Assert.Equal(UmkaTypeKind.SignedInteger, callable.ResultType.Kind);
        Assert.True(callable.CanCallWith(UmkaValue.From(5)));
        Assert.False(callable.CanCallWith(UmkaValue.From("bad")));

        Assert.Equal(15, callable.CallInt64(UmkaValue.From(5)));
        Assert.Equal(17, callable.CallValue(UmkaValue.From(7)).AsInt64());
    }

    [Fact]
    public void Callable_payload_deconstructed_from_any_can_be_invoked()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeLabelScorer(prefix: str): fn (x: int): int {
                return fn (x: int): int |prefix| {
                    return len(prefix) + x
                }
            }

            fn makeAnyCallable*(): any {
                return makeLabelScorer("abcd")
            }
            """);

        runtime.Compile();

        var any = runtime.GetFunction("makeAnyCallable").CallAny();
        Assert.False(any.IsNull);
        Assert.NotNull(any.PayloadType);
        Assert.True(any.PayloadType!.IsCallable);

        using var retained = any.Payload.AsNativeValue();
        var callable = retained.AsCallable();

        Assert.Equal(6, callable.CallInt64(UmkaValue.From(2)));
    }

    [Fact]
    public void Callable_wrapper_rejects_disposed_retained_value()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeAdder*(base: int): fn (x: int): int {
                return fn (x: int): int |base| {
                    return base + x
                }
            }
            """);

        runtime.Compile();

        var retained = runtime.GetFunction("makeAdder").CallNativeValue(UmkaValue.From(10));
        var callable = retained.AsCallable();
        retained.Dispose();

        Assert.False(callable.CanCallWith(UmkaValue.From(1)));
        Assert.Throws<ObjectDisposedException>(() => callable.CallInt64(UmkaValue.From(1)));
    }

    [Fact]
    public void Callable_runtime_errors_terminate_runtime_and_surface_umka_error()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeFailingCallable*(): fn (x: int): int {
                return fn (x: int): int {
                    a := []int{}
                    return a[1] + x
                }
            }
            """);

        runtime.Compile();

        using var retained = runtime.GetFunction("makeFailingCallable").CallNativeValue();
        var callable = retained.AsCallable();

        var error = Assert.Throws<UmkaException>(() => callable.CallInt64(UmkaValue.From(1)));
        Assert.Contains("Dynamic array", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.IsAlive);
        Assert.Throws<InvalidOperationException>(() => callable.CallInt64(UmkaValue.From(1)));
    }

    [Fact]
    public void Callable_invocation_observes_pending_interrupt()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn makeAdder*(base: int): fn (x: int): int {
                return fn (x: int): int |base| {
                    return base + x
                }
            }
            """);

        runtime.Compile();

        using var retained = runtime.GetFunction("makeAdder").CallNativeValue(UmkaValue.From(10));
        var callable = retained.AsCallable();

        runtime.RequestInterrupt("stop callable");
        var error = Assert.Throws<UmkaException>(() => callable.CallInt64(UmkaValue.From(1)));
        Assert.Contains("stop callable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.IsAlive);
    }

    [Fact]
    public void Fiber_retained_values_are_not_callable()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn fiberValue*(): fiber {
                return make(fiber, fn() {})
            }
            """);

        runtime.Compile();

        var fiber = runtime.GetFunction("fiberValue");
        Assert.Equal(UmkaTypeKind.Fiber, fiber.ResultType.Kind);
        Assert.False(fiber.ResultType.IsCallable);

        Assert.Throws<InvalidOperationException>(() => fiber.CallNativeValue());
    }
}
