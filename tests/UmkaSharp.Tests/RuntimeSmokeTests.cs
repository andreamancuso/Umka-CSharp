using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeSmokeTests
{
    private static readonly string[] CommandLineArguments = ["script.um", "alpha", "zażółć"];

    private const string FileWriterSource = """
        import "std.um"

        fn writeText*(name: str): int {
            f, err := std::fopen(name, "wb")
            if err.code != 0 {
                return err.code
            }

            data := []char("Hello from UmkaSharp")
            _, writeErr := std::fwrite(f, &data)
            if writeErr.code != 0 {
                std::fclose(f)
                return writeErr.code
            }

            closeErr := std::fclose(f)
            return closeErr.code
        }
        """;

    [Fact]
    public void Runtime_can_call_umka_function_with_primitive_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

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
    public void Runtime_compile_source_convenience_returns_compiled_runtime()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource(
            """
            import "std.um"

            fn count*(): int {
                return std::argc()
            }

            fn answer*(): int {
                return 42
            }
            """,
            new UmkaRuntimeOptions
            {
                Arguments = CommandLineArguments,
            });

        Assert.Equal(UmkaRuntimeState.Compiled, runtime.State);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        Assert.Equal(3, runtime.GetFunction("count").CallInt64());
    }

    [Fact]
    public void Runtime_compile_source_convenience_can_configure_modules_and_callbacks()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.CompileSource(
            """
            import "host.um"

            fn answer*(): int {
                return host::doubleIt(21)
            }
            """,
            configure: configured =>
            {
                configured.AddModule("host.um", "fn doubleIt*(x: int): int");
                configured.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
            });

        Assert.Equal(UmkaRuntimeState.Compiled, runtime.State);
        Assert.Equal(["host.um"], runtime.RegisteredModuleNames);
        Assert.Equal(["doubleIt"], runtime.RegisteredCallbackNames);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
    }

    [Fact]
    public void Runtime_compile_source_convenience_disposes_runtime_when_configuration_fails()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            UmkaRuntime.CompileSource(
                """
                fn answer*(): int {
                    return 42
                }
                """,
                configure: runtime =>
                {
                    configuredRuntime = runtime;
                    throw new InvalidOperationException("configuration failed");
                }));

        Assert.Equal("configuration failed", ex.Message);
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void Runtime_can_load_source_from_file_with_sibling_imports()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "main.um");
            File.WriteAllText(mainFile, """
                import "math.um"

                fn answer*(): int {
                    return math::inc(41)
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "math.um"), """
                fn inc*(value: int): int {
                    return value + 1
                }
                """);

            using var runtime = UmkaRuntime.FromFile(mainFile);

            runtime.Compile();

            Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_compile_file_convenience_returns_compiled_runtime()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "main.um");
            File.WriteAllText(mainFile, """
                import "math.um"

                fn answer*(): int {
                    return math::inc(41)
                }
                """);
            File.WriteAllText(Path.Combine(tempDir, "math.um"), """
                fn inc*(value: int): int {
                    return value + 1
                }
                """);

            using var runtime = UmkaRuntime.CompileFile(mainFile);

            Assert.Equal(UmkaRuntimeState.Compiled, runtime.State);
            Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_compile_file_convenience_can_configure_modules_and_callbacks()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "main.um");
            File.WriteAllText(mainFile, """
                import "host.um"

                fn answer*(): int {
                    return host::doubleIt(21)
                }
                """);

            using var runtime = UmkaRuntime.CompileFile(
                mainFile,
                configure: configured =>
                {
                    configured.AddModule("host.um", "fn doubleIt*(x: int): int");
                    configured.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
                });

            Assert.Equal(UmkaRuntimeState.Compiled, runtime.State);
            Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_passes_command_line_arguments_to_umka_stdlib()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "std.um"

            fn count*(): int {
                return std::argc()
            }

            fn arg*(index: int): str {
                return std::argv(index)
            }
            """, arguments: CommandLineArguments);

        runtime.Compile();

        Assert.Equal(3, runtime.GetFunction("count").CallInt64());
        Assert.Equal("script.um", runtime.GetFunction("arg").CallString(UmkaValue.From(0)));
        Assert.Equal("alpha", runtime.GetFunction("arg").CallString(UmkaValue.From(1)));
        Assert.Equal("zażółć", runtime.GetFunction("arg").CallString(UmkaValue.From(2)));
    }

    [Fact]
    public void Runtime_accepts_creation_options()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource(
            """
                import "std.um"

                fn count*(): int {
                    return std::argc()
                }

                fn arg*(index: int): str {
                    return std::argv(index)
                }
                """,
            new UmkaRuntimeOptions
            {
                StackSize = UmkaRuntime.DefaultStackSize,
                Arguments = CommandLineArguments,
            });

        runtime.Compile();

        Assert.Equal(3, runtime.GetFunction("count").CallInt64());
        Assert.Equal("script.um", runtime.GetFunction("arg").CallString(UmkaValue.From(0)));
    }

    [Fact]
    public void Runtime_exposes_creation_metadata_snapshots()
    {
        NativeTestEnvironment.RequireNativeShim();

        var arguments = new[] { "configured.um", "alpha" };
        var options = new UmkaRuntimeOptions
        {
            StackSize = UmkaRuntime.DefaultStackSize + 128,
            FileSystemEnabled = true,
            ImplementationLibrariesEnabled = true,
            Arguments = arguments,
        };

        arguments[1] = "mutated";

        using var runtime = UmkaRuntime.FromSource(
            """
            fn answer*(): int {
                return 42
            }
            """,
            "configured.um",
            options);

        Assert.Equal("configured.um", runtime.SourceFileName);
        Assert.Equal(UmkaRuntime.DefaultStackSize + 128, runtime.StackSize);
        Assert.True(runtime.FileSystemEnabled);
        Assert.True(runtime.ImplementationLibrariesEnabled);
        Assert.Equal(["configured.um", "alpha"], runtime.Arguments);

        runtime.Compile();
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());

        runtime.Dispose();

        Assert.Equal("configured.um", runtime.SourceFileName);
        Assert.Equal(UmkaRuntime.DefaultStackSize + 128, runtime.StackSize);
        Assert.Equal(["configured.um", "alpha"], runtime.Arguments);
    }

    [Fact]
    public void Runtime_file_system_option_controls_std_file_io()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var outputFile = Path.Combine(tempDir, "file-system-enabled.txt");
        try
        {
            using (var sandboxed = UmkaRuntime.FromSource(FileWriterSource))
            {
                sandboxed.Compile();

                var sandboxedResult = sandboxed.GetFunction("writeText").CallInt64(UmkaValue.From(outputFile));

                Assert.NotEqual(0, sandboxedResult);
                Assert.False(File.Exists(outputFile));
            }

            using var enabled = UmkaRuntime.FromSource(
                FileWriterSource,
                new UmkaRuntimeOptions
                {
                    FileSystemEnabled = true,
                });

            enabled.Compile();

            Assert.Equal(0, enabled.GetFunction("writeText").CallInt64(UmkaValue.From(outputFile)));
            Assert.Equal("Hello from UmkaSharp", File.ReadAllText(outputFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_run_executes_main_entry_point()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn main() {
                host::record(42)
            }
            """);

        runtime.AddModule("host.um", "fn record*(value: int)");

        var observed = 0L;
        runtime.Register("record", frame =>
        {
            observed = frame.GetInt64(0);
            return UmkaValue.Void;
        });

        runtime.Compile();
        runtime.Run();

        Assert.Equal(42, observed);
    }

    [Fact]
    public void Runtime_run_source_convenience_executes_main_and_disposes_runtime()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;
        var observed = 0L;

        UmkaRuntime.RunSource(
            """
            import "host.um"

            fn main() {
                host::record(42)
            }
            """,
            configure: runtime =>
            {
                configuredRuntime = runtime;
                runtime.AddModule("host.um", "fn record*(value: int)");
                runtime.Register("record", frame =>
                {
                    observed = frame.GetInt64(0);
                    return UmkaValue.Void;
                });
            });

        Assert.Equal(42, observed);
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void Runtime_can_call_managed_callback_from_umka()
    {
        NativeTestEnvironment.RequireNativeShim();

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
    public void Runtime_can_execute_umka_scripts_that_use_fibers_internally()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn mkfunc(result: ^int): fiber {
                return make(fiber, |result| {
                    result^ = 42
                })
            }

            fn run*(): int {
                result := 0
                fiberValue := mkfunc(&result)
                resume(fiberValue)
                return result
            }
            """);

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Runtime_can_roundtrip_strings()
    {
        NativeTestEnvironment.RequireNativeShim();

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
        NativeTestEnvironment.RequireNativeShim();

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
        NativeTestEnvironment.RequireNativeShim();

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
        NativeTestEnvironment.RequireNativeShim();

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
        var callbackException = Assert.IsType<InvalidOperationException>(callback.LastException);
        Assert.Same(callbackException, ex.InnerException);
    }

    [Fact]
    public void Runtime_surfaces_compile_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("fn broken*( {");

        var ex = Assert.Throws<UmkaException>(() => runtime.Compile());
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
    }

    [Fact]
    public void Runtime_rejects_double_compile()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealPair
    {
        public double X;
        public double Y;
    }
}
