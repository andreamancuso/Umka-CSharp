using Xunit;

namespace UmkaSharp.Tests;

public sealed class AnyValueTests
{
    [Fact]
    public void Function_deconstructs_scalar_string_and_null_any_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            fn intAny*(): any {
                return 42
            }

            fn realAny*(): any {
                return 2.5
            }

            fn boolAny*(): any {
                return true
            }

            fn stringAny*(): any {
                return "hello"
            }

            fn nullAny*(): any {
                return null
            }
            """);

        var intAny = runtime.GetFunction("intAny");
        Assert.True(intAny.ResultType.IsAny);
        Assert.True(intAny.CanReadResultAsAny());
        Assert.True(intAny.CanReadResultAsValue());

        var intValue = intAny.CallAny();
        Assert.False(intValue.IsNull);
        Assert.Equal(UmkaTypeKind.SignedInteger, intValue.PayloadType?.Kind);
        Assert.Equal(42, intValue.Payload.AsInt64());

        var dynamicValue = intAny.CallValue();
        Assert.Equal(UmkaValueKind.Any, dynamicValue.Kind);
        Assert.Equal(42, dynamicValue.AsAny().Payload.AsInt64());

        Assert.Equal(2.5, runtime.GetFunction("realAny").CallAny().Payload.AsDouble());
        Assert.True(runtime.GetFunction("boolAny").CallAny().Payload.AsBoolean());
        Assert.Equal("hello", runtime.GetFunction("stringAny").CallAny().Payload.AsString());
        Assert.True(runtime.GetFunction("nullAny").CallAny().IsNull);
        Assert.True(runtime.GetFunction("nullAny").TryCallAny(out var nullValue));
        Assert.NotNull(nullValue);
        Assert.True(nullValue.IsNull);
    }

    [Fact]
    public void Function_constructs_any_arguments_from_managed_scalars_and_retained_native_payloads()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource("""
            fn scores*(): map[str]int {
                return map[str]int{"a": 7}
            }

            fn score*(value: any): int {
                switch x := type(value) {
                    case int: return x
                    case real: return trunc(x * 10.0)
                    case bool:
                        if x {return 1}
                        return 0
                    case str: return len(x)
                    case map[str]int: return x["a"]
                    default: return -1
                }
                return -1
            }

            fn validAny*(value: any): bool {
                return valid(value)
            }
            """);

        var score = runtime.GetFunction("score");
        Assert.True(score.ParameterTypes[0].IsAny);

        Assert.Equal(42, score.CallInt64(UmkaAnyValue.From(42).ToValue()));
        Assert.Equal(25, score.CallInt64(UmkaAnyValue.From(2.5).ToValue()));
        Assert.Equal(1, score.CallInt64(UmkaAnyValue.From(true).ToValue()));
        Assert.Equal(5, score.CallInt64(UmkaAnyValue.From("hello").ToValue()));
        Assert.False(runtime.GetFunction("validAny").CallBoolean(UmkaAnyValue.Null.ToValue()));

        using var retainedScores = runtime.GetFunction("scores").CallNativeValue();
        Assert.Equal(7, score.CallInt64(UmkaAnyValue.From(retainedScores).ToValue()));
    }

    [Fact]
    public void Callback_reads_any_arguments_and_returns_any_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn score(value: any): int {
                switch x := type(value) {
                    case str: return len(x)
                    default: return -1
                }
                return -1
            }

            fn nullScore(value: any): int {
                if valid(value) {
                    return -1
                }
                return 5
            }

            fn run*(): int {
                return host::inspect(42) + score(host::makeAny("hello")) + nullScore(host::makeNull())
            }
            """);

        runtime.AddModule("host.um", """
            fn inspect*(value: any): int
            fn makeAny*(value: str): any
            fn makeNull*(): any
            """);

        runtime.Register("inspect", frame =>
        {
            Assert.True(frame.CanReadArgumentAsAny(0));

            var any = frame.GetAny(0);
            Assert.Equal(UmkaTypeKind.SignedInteger, any.PayloadType?.Kind);
            Assert.Equal(42, any.Payload.AsInt64());

            var dynamicValue = frame.GetValue(0);
            Assert.Equal(UmkaValueKind.Any, dynamicValue.Kind);
            Assert.Equal(42, dynamicValue.AsAny().Payload.AsInt64());

            Assert.True(frame.TryGetAny(0, out var tryAny));
            Assert.NotNull(tryAny);
            Assert.Equal(42, tryAny.Payload.AsInt64());

            return UmkaValue.From(10);
        });

        runtime.Register("makeAny", frame =>
        {
            Assert.True(frame.ResultType.IsAny);
            var result = UmkaAnyValue.From(frame.GetString(0)).ToValue();
            Assert.True(frame.CanReturn(result));
            return result;
        });

        runtime.Register("makeNull", frame => UmkaAnyValue.Null.ToValue());

        runtime.Compile();

        Assert.Equal(20, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Any_construction_rejects_managed_aggregates_without_concrete_umka_type_metadata()
    {
        var ex = Assert.Throws<NotSupportedException>(() =>
            UmkaAnyValue.From(UmkaValue.FromDynamicArray(1L, 2L, 3L)));

        Assert.Contains("concrete Umka type metadata", ex.Message);
        Assert.Contains("Retain an Umka value", ex.Message);
    }
}
