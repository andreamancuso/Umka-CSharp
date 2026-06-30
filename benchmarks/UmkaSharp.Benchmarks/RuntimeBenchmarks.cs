using BenchmarkDotNet.Attributes;

namespace UmkaSharp.Benchmarks;

/// <summary>Measures runtime creation, compilation, and deterministic cleanup.</summary>
[MemoryDiagnoser]
#pragma warning disable CA1822 // BenchmarkDotNet requires benchmark methods to be instance methods.
public class RuntimeBenchmarks
{
    private const string MinimalSource = """
        fn answer*(): int {
            return 42
        }
        """;

    private const string ModuleSource = """
        import "math.um"

        fn answer*(): int {
            return math::inc(41)
        }
        """;

    private const string MathModule = """
        fn inc*(value: int): int {
            return value + 1
        }
        """;

    [Benchmark]
    public void CreateCompileDispose()
    {
        using var runtime = UmkaRuntime.FromSource(MinimalSource);
        runtime.Compile();
    }

    [Benchmark]
    public void CreateModuleCompileDispose()
    {
        using var runtime = UmkaRuntime.FromSource(ModuleSource);
        runtime.AddModule("math.um", MathModule);
        runtime.Compile();
    }
}
#pragma warning restore CA1822
