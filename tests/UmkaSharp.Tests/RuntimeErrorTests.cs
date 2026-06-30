using Xunit;

namespace UmkaSharp.Tests;

public sealed class RuntimeErrorTests
{
    [Fact]
    public void Runtime_surfaces_missing_function_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("missing"));

        Assert.Contains("missing", ex.Message);
        Assert.Contains("not found", ex.Message);
        Assert.Null(ex.Error.FileName);
        Assert.Equal("missing", ex.Error.FunctionName);
        Assert.Equal(2, ex.Error.Code);
        Assert.Contains("missing", ex.Error.Message);
    }

    [Fact]
    public void Runtime_can_try_get_optional_functions()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        Assert.True(runtime.TryGetFunction("answer", out var answer));
        Assert.NotNull(answer);
        Assert.Equal(42, answer.CallInt64());

        Assert.False(runtime.TryGetFunction("optionalHook", out var optionalHook));
        Assert.Null(optionalHook);
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, answer.CallInt64());
    }

    [Fact]
    public void Runtime_does_not_lookup_non_exported_root_source_functions()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn hidden(): int {
                return 41
            }

            fn answer*(): int {
                return hidden() + 1
            }
            """);

        runtime.Compile();

        Assert.False(runtime.TryGetFunction("hidden", out var hidden));
        Assert.Null(hidden);
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("hidden"));

        Assert.Contains("hidden", ex.Message);
        Assert.Null(ex.Error.FileName);
        Assert.Equal("hidden", ex.Error.FunctionName);
        Assert.Equal(2, ex.Error.Code);
    }

    [Fact]
    public void Runtime_surfaces_umka_runtime_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        var ex = Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.False(runtime.IsAlive);
    }

    [Fact]
    public void Runtime_try_call_scalar_preserves_umka_runtime_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        var ex = Assert.Throws<UmkaException>(() => divide.TryCallScalar<int>(out _, UmkaValue.From(0)));

        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.False(runtime.IsAlive);
    }

    [Fact]
    public void Runtime_try_call_void_preserves_umka_runtime_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        var ex = Assert.Throws<UmkaException>(() => divide.TryCallVoid(UmkaValue.From(0)));

        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.False(runtime.IsAlive);
    }

    [Fact]
    public void Runtime_formats_terminated_state_after_runtime_error()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        Assert.Equal("UmkaRuntime(Compiled)", runtime.ToString());

        Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        Assert.Equal("UmkaRuntime(Terminated)", runtime.ToString());
    }

    [Fact]
    public void Compile_errors_include_source_location_and_code()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("fn broken*( {", fileName: "broken.um");

        var ex = Assert.Throws<UmkaException>(() => runtime.Compile());

        Assert.EndsWith("broken.um", ex.Error.FileName);
        Assert.True(ex.Error.Line > 0);
        Assert.True(ex.Error.Position >= 0);
        Assert.NotEqual(0, ex.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.Contains("broken.um", ex.Message);
    }

    [Fact]
    public void Runtime_can_try_compile_successfully()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.True(runtime.TryCompile(out var error));

        Assert.Null(error);
        Assert.Equal("UmkaRuntime(Compiled)", runtime.ToString());
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        Assert.Throws<InvalidOperationException>(() => runtime.TryCompile(out _));
    }

    [Fact]
    public void TryCompile_returns_error_without_throwing_for_umka_compile_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("fn broken*( {", fileName: "broken.um");

        Assert.False(runtime.TryCompile(out var error));

        Assert.NotNull(error);
        Assert.EndsWith("broken.um", error.FileName);
        Assert.True(error.Line > 0);
        Assert.True(error.Position >= 0);
        Assert.NotEqual(0, error.Code);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.False(runtime.IsAlive);
        Assert.Equal("UmkaRuntime(CompileAttempted)", runtime.ToString());
        Assert.Throws<InvalidOperationException>(() => runtime.TryCompile(out _));
        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
    }

    [Fact]
    public void TryCompileSource_returns_compiled_runtime_on_success()
    {
        NativeTestEnvironment.RequireNativeShim();

        var success = UmkaRuntime.TryCompileSource(
            """
            import "host.um"

            fn answer*(): int {
                return host::doubleIt(21)
            }
            """,
            out var runtime,
            out var error,
            configure: configured =>
            {
                configured.AddModule("host.um", "fn doubleIt*(x: int): int");
                configured.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
            });

        Assert.True(success);
        Assert.Null(error);
        using var compiledRuntime = Assert.IsType<UmkaRuntime>(runtime);
        Assert.Equal(UmkaRuntimeState.Compiled, compiledRuntime.State);
        Assert.Equal(42, compiledRuntime.GetFunction("answer").CallInt64());
    }

    [Fact]
    public void TryCompileSource_returns_error_and_disposes_runtime_for_compile_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;

        var success = UmkaRuntime.TryCompileSource(
            "fn broken*( {",
            "broken.um",
            out var runtime,
            out var error,
            configure: configured => configuredRuntime = configured);

        Assert.False(success);
        Assert.Null(runtime);
        Assert.NotNull(error);
        Assert.EndsWith("broken.um", error.FileName);
        Assert.True(error.Line > 0);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void CompileSource_disposes_runtime_when_compile_errors_throw()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;

        var ex = Assert.Throws<UmkaException>(() =>
            UmkaRuntime.CompileSource(
                "fn broken*( {",
                "broken.um",
                options: null,
                configure: runtime => configuredRuntime = runtime));

        Assert.EndsWith("broken.um", ex.Error.FileName);
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void CompileSource_disposes_runtime_when_warning_handler_throws()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;
        var handlerException = new InvalidOperationException("warning handler failed");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            UmkaRuntime.CompileSource(
                """
                fn answer*(): int {
                    unused := 1
                    return 42
                }
                """,
                new UmkaRuntimeOptions
                {
                    WarningHandler = _ => throw handlerException,
                },
                configure: runtime => configuredRuntime = runtime));

        Assert.Equal("Umka warning handler failed.", ex.Message);
        Assert.Same(handlerException, ex.InnerException);
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void TryCompileSource_disposes_runtime_when_warning_handler_throws()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;
        var handlerException = new InvalidOperationException("warning handler failed");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            UmkaRuntime.TryCompileSource(
                """
                fn answer*(): int {
                    unused := 1
                    return 42
                }
                """,
                out _,
                out _,
                new UmkaRuntimeOptions
                {
                    WarningHandler = _ => throw handlerException,
                },
                configure: runtime => configuredRuntime = runtime));

        Assert.Equal("Umka warning handler failed.", ex.Message);
        Assert.Same(handlerException, ex.InnerException);
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void TryCompileFile_returns_compiled_runtime_on_success()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "main.um");
            File.WriteAllText(mainFile, """
                fn answer*(): int {
                    return 42
                }
                """);

            var success = UmkaRuntime.TryCompileFile(mainFile, out var runtime, out var error);

            Assert.True(success);
            Assert.Null(error);
            using var compiledRuntime = Assert.IsType<UmkaRuntime>(runtime);
            Assert.Equal(42, compiledRuntime.GetFunction("answer").CallInt64());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryCompileFile_returns_error_and_disposes_runtime_for_compile_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainFile = Path.Combine(tempDir, "broken.um");
            File.WriteAllText(mainFile, "fn broken*( {");
            UmkaRuntime? configuredRuntime = null;

            var success = UmkaRuntime.TryCompileFile(
                mainFile,
                out var runtime,
                out var error,
                configure: configured => configuredRuntime = configured);

            Assert.False(success);
            Assert.Null(runtime);
            Assert.NotNull(error);
            Assert.EndsWith("broken.um", error.FileName);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
            Assert.NotNull(configuredRuntime);
            Assert.True(configuredRuntime.IsDisposed);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void TryCompile_rethrows_warning_handler_exceptions_after_native_compile_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        var handlerException = new InvalidOperationException("warning handler failed");
        using var runtime = UmkaRuntime.FromSource(
            """
            fn answer*(): int {
                unused := 1
                return 42
            }
            """,
            new UmkaRuntimeOptions
            {
                WarningHandler = _ => throw handlerException,
            });

        var ex = Assert.Throws<InvalidOperationException>(() => runtime.TryCompile(out _));

        Assert.Equal("Umka warning handler failed.", ex.Message);
        Assert.Same(handlerException, ex.InnerException);
        Assert.Equal("UmkaRuntime(Compiled)", runtime.ToString());
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        Assert.Throws<InvalidOperationException>(() => runtime.TryCompile(out _));
    }

    [Fact]
    public void Compile_warnings_are_reported_to_runtime_options_handler()
    {
        NativeTestEnvironment.RequireNativeShim();

        var warnings = new List<UmkaError>();
        using var runtime = UmkaRuntime.FromSource(
            """
            fn answer*(): int {
                unused := 1
                return 42
            }
            """,
            new UmkaRuntimeOptions
            {
                WarningHandler = warnings.Add,
            });

        runtime.Compile();

        var warning = Assert.Single(warnings);
        Assert.EndsWith("main.um", warning.FileName);
        Assert.Equal("answer", warning.FunctionName);
        Assert.True(warning.Line > 0);
        Assert.True(warning.Position > 0);
        Assert.Equal(0, warning.Code);
        Assert.Contains("not used", warning.Message);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
    }

    [Fact]
    public void Compile_warning_handler_exceptions_are_rethrown_after_native_compile_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        var handlerException = new InvalidOperationException("warning handler failed");
        using var runtime = UmkaRuntime.FromSource(
            """
            fn answer*(): int {
                unused := 1
                return 42
            }
            """,
            new UmkaRuntimeOptions
            {
                WarningHandler = _ => throw handlerException,
            });

        var ex = Assert.Throws<InvalidOperationException>(() => runtime.Compile());

        Assert.Equal("Umka warning handler failed.", ex.Message);
        Assert.Same(handlerException, ex.InnerException);
        Assert.Equal("UmkaRuntime(Compiled)", runtime.ToString());
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
        Assert.Throws<InvalidOperationException>(() => runtime.Compile());
        Assert.Throws<InvalidOperationException>(() => runtime.AddModule("late.um", "fn late*(): int"));
        Assert.Throws<InvalidOperationException>(() => runtime.Register("late", _ => UmkaValue.Void));
    }

    [Fact]
    public void Runtime_errors_include_source_function_location_and_code()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """, fileName: "runtime-error.um");

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        var ex = Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        Assert.EndsWith("runtime-error.um", ex.Error.FileName);
        Assert.Equal("divide", ex.Error.FunctionName);
        Assert.True(ex.Error.Line > 0);
        Assert.True(ex.Error.Position >= 0);
        Assert.NotEqual(0, ex.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.Contains("runtime-error.um", ex.Message);
        Assert.Contains("divide", ex.Message);
    }

    [Fact]
    public void Runtime_rejects_execution_after_runtime_error()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide*(x: int): int {
                return 42 / x
            }
            """);

        runtime.Compile();
        var divide = runtime.GetFunction("divide");

        Assert.Throws<UmkaException>(() => divide.CallInt64(UmkaValue.From(0)));

        Assert.False(runtime.IsAlive);
        Assert.Equal(UmkaRuntimeState.Terminated, runtime.State);
        Assert.False(string.IsNullOrWhiteSpace(runtime.GetLastError().Message));
        Assert.True(runtime.TryGetLastError(out var error));
        Assert.NotNull(error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
        Assert.Throws<InvalidOperationException>(() => divide.CallInt64(UmkaValue.From(1)));
        Assert.Throws<InvalidOperationException>(() => runtime.Run());
        Assert.Throws<InvalidOperationException>(() => runtime.TryRun(out _));
        Assert.Throws<InvalidOperationException>(() => runtime.GetFunction("divide"));
        Assert.Throws<InvalidOperationException>(() => runtime.TryGetFunction("divide", out _));
    }

    [Fact]
    public void Runtime_can_try_run_successfully()
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

        Assert.True(runtime.TryRun(out var exception));

        Assert.Null(exception);
        Assert.Equal(42, observed);
        Assert.True(runtime.IsAlive);
    }

    [Fact]
    public void Run_errors_include_source_function_location_and_code()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide(x: int): int {
                return 42 / x
            }

            fn main() {
                divide(0)
            }
            """, fileName: "run-error.um");

        runtime.Compile();

        var ex = Assert.Throws<UmkaException>(() => runtime.Run());

        Assert.EndsWith("run-error.um", ex.Error.FileName);
        Assert.Equal("divide", ex.Error.FunctionName);
        Assert.True(ex.Error.Line > 0);
        Assert.True(ex.Error.Position >= 0);
        Assert.NotEqual(0, ex.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
        Assert.Contains("run-error.um", ex.Message);
        Assert.Contains("divide", ex.Message);
    }

    [Fact]
    public void TryRun_returns_exception_without_throwing_for_umka_runtime_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn divide(x: int): int {
                return 42 / x
            }

            fn main() {
                divide(0)
            }
            """, fileName: "try-run-error.um");

        runtime.Compile();

        Assert.False(runtime.TryRun(out var exception));

        Assert.NotNull(exception);
        Assert.EndsWith("try-run-error.um", exception.Error.FileName);
        Assert.Equal("divide", exception.Error.FunctionName);
        Assert.True(exception.Error.Line > 0);
        Assert.True(exception.Error.Position >= 0);
        Assert.NotEqual(0, exception.Error.Code);
        Assert.False(string.IsNullOrWhiteSpace(exception.Error.Message));
        Assert.Contains("try-run-error.um", exception.Message);
        Assert.Contains("divide", exception.Message);
        Assert.False(runtime.IsAlive);
        Assert.Equal("UmkaRuntime(Terminated)", runtime.ToString());
        Assert.Throws<InvalidOperationException>(() => runtime.TryRun(out _));
    }

    [Fact]
    public void TryRunSource_returns_compile_errors_as_exception_and_disposes_runtime()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;

        Assert.False(UmkaRuntime.TryRunSource(
            "fn broken*( {",
            out var exception,
            configure: runtime => configuredRuntime = runtime));

        Assert.NotNull(exception);
        Assert.False(string.IsNullOrWhiteSpace(exception.Error.Message));
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void TryRunSource_returns_runtime_errors_as_exception_and_disposes_runtime()
    {
        NativeTestEnvironment.RequireNativeShim();

        UmkaRuntime? configuredRuntime = null;
        UmkaCallback? callback = null;

        Assert.False(UmkaRuntime.TryRunSource(
            """
            import "host.um"

            fn main() {
                host::explode()
            }
            """,
            out var exception,
            configure: runtime =>
            {
                configuredRuntime = runtime;
                runtime.AddModule("host.um", "fn explode*()");
                callback = runtime.Register("explode", _ => throw new InvalidOperationException("boom"));
            }));

        Assert.NotNull(exception);
        Assert.NotNull(callback);
        var callbackException = Assert.IsType<InvalidOperationException>(callback.LastException);
        Assert.Same(callbackException, exception.InnerException);
        Assert.Contains("Managed callback failed", exception.Error.Message);
        Assert.NotNull(configuredRuntime);
        Assert.True(configuredRuntime.IsDisposed);
    }

    [Fact]
    public void Run_callback_failures_include_managed_inner_exception()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn main() {
                host::explode()
            }
            """, fileName: "callback-run-error.um");

        runtime.AddModule("host.um", "fn explode*()");
        var callback = runtime.Register("explode", _ => throw new InvalidOperationException("boom"));

        runtime.Compile();

        Assert.Null(runtime.LastCallbackException);

        var ex = Assert.Throws<UmkaException>(() => runtime.Run());
        var callbackException = Assert.IsType<InvalidOperationException>(callback.LastException);

        Assert.Same(callbackException, ex.InnerException);
        Assert.Same(callbackException, runtime.LastCallbackException);
        Assert.Contains("Managed callback failed", ex.Error.Message);
        Assert.False(runtime.IsAlive);
        Assert.Equal("UmkaRuntime(Terminated)", runtime.ToString());
        Assert.Contains("Managed callback failed", runtime.GetLastError().Message);
        Assert.True(runtime.TryGetLastError(out var lastError));
        Assert.NotNull(lastError);
        Assert.Contains("Managed callback failed", lastError.Message);
        Assert.Throws<InvalidOperationException>(() => runtime.Run());
    }

    [Fact]
    public void TryRun_callback_failures_include_managed_inner_exception()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn main() {
                host::explode()
            }
            """, fileName: "callback-try-run-error.um");

        runtime.AddModule("host.um", "fn explode*()");
        var callback = runtime.Register("explode", _ => throw new InvalidOperationException("boom"));

        runtime.Compile();

        Assert.Null(runtime.LastCallbackException);

        Assert.False(runtime.TryRun(out var exception));
        var callbackException = Assert.IsType<InvalidOperationException>(callback.LastException);

        Assert.NotNull(exception);
        Assert.Same(callbackException, exception.InnerException);
        Assert.Same(callbackException, runtime.LastCallbackException);
        Assert.Contains("Managed callback failed", exception.Error.Message);
        Assert.False(runtime.IsAlive);
        Assert.Equal("UmkaRuntime(Terminated)", runtime.ToString());
        Assert.Contains("Managed callback failed", runtime.GetLastError().Message);
        Assert.True(runtime.TryGetLastError(out var lastError));
        Assert.NotNull(lastError);
        Assert.Contains("Managed callback failed", lastError.Message);
        Assert.Throws<InvalidOperationException>(() => runtime.TryRun(out _));
    }

    [Fact]
    public void UmkaException_validates_public_constructor_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => new UmkaException((UmkaError)null!));
        Assert.Throws<ArgumentNullException>(() => new UmkaException((UmkaError)null!, new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => new UmkaException((string)null!));
    }

    [Fact]
    public void UmkaException_formats_managed_errors_without_source_coordinates()
    {
        var fileOnly = new UmkaException(new UmkaError("math.um", null, 0, 0, 2, "Missing module."));
        var functionOnly = new UmkaException(new UmkaError(null, "addFee", 0, 0, 2, "Missing function."));
        var fileAndFunction = new UmkaException(new UmkaError("math.um", "addFee", 0, 0, 2, "Missing module function."));

        Assert.Equal("Missing module. (math.um)", fileOnly.Message);
        Assert.Equal("Missing function. (function addFee)", functionOnly.Message);
        Assert.Equal("Missing module function. (math.um; function addFee)", fileAndFunction.Message);
    }

    [Fact]
    public void Function_rejects_void_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn id*(x: int): int {
                return x
            }
            """);

        runtime.Compile();
        var id = runtime.GetFunction("id");

        Assert.Throws<ArgumentException>(() => id.CallInt64(UmkaValue.Void));
    }

    [Fact]
    public void Function_rejects_missing_and_extra_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn add*(a, b: int): int {
                return a + b
            }
            """);

        runtime.Compile();
        var add = runtime.GetFunction("add");

        Assert.Equal(2, add.ParameterCount);
        Assert.Equal(42, add.CallInt64(UmkaValue.From(19), UmkaValue.From(23)));
        Assert.Throws<ArgumentException>(() => add.CallInt64(UmkaValue.From(19)));
        Assert.Throws<ArgumentException>(() => add.CallInt64(
            UmkaValue.From(19),
            UmkaValue.From(23),
            UmkaValue.From(1)));
    }

    [Fact]
    public void Function_accepts_omitted_supported_umka_default_parameters()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn add*(value: int, bonus: int = 5, scale: real = 2.0): real {
                return real(value + bonus) * scale
            }

            fn greet*(name: str, suffix: str = "!"): str {
                return name + suffix
            }
            """);

        runtime.Compile();
        var add = runtime.GetFunction("add");
        var greet = runtime.GetFunction("greet");

        Assert.Equal(3, add.ParameterCount);
        Assert.Equal(1, add.RequiredParameterCount);
        Assert.Equal(2, add.DefaultParameterCount);
        Assert.True(add.CanCallWith(UmkaValue.From(16)));
        Assert.True(add.CanCallWith(UmkaValue.From(16), UmkaValue.From(5)));
        Assert.True(add.CanCallWith(UmkaValue.From(16), UmkaValue.From(5), UmkaValue.From(2.0)));
        Assert.False(add.CanCallWith());
        Assert.False(add.CanCallWith(
            UmkaValue.From(16),
            UmkaValue.From(5),
            UmkaValue.From(2.0),
            UmkaValue.From(1)));
        Assert.Equal(42.0, add.CallDouble(UmkaValue.From(16)));
        Assert.Equal(44.0, add.CallDouble(UmkaValue.From(17), UmkaValue.From(5)));
        Assert.Equal(66.0, add.CallDouble(UmkaValue.From(17), UmkaValue.From(5), UmkaValue.From(3.0)));

        Assert.Equal(2, greet.ParameterCount);
        Assert.Equal(1, greet.RequiredParameterCount);
        Assert.Equal(1, greet.DefaultParameterCount);
        Assert.Equal("Umka!", greet.CallString(UmkaValue.From("Umka")));
        Assert.Equal("Umka?", greet.CallString(UmkaValue.From("Umka"), UmkaValue.From("?")));
    }

    [Fact]
    public void Function_rejects_omitted_defaults_for_unsupported_parameter_types()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn ignore*(value: any = null): int {
                return 42
            }
            """);

        runtime.Compile();
        var ignore = runtime.GetFunction("ignore");

        Assert.Equal(1, ignore.ParameterCount);
        Assert.Equal(0, ignore.RequiredParameterCount);
        Assert.Equal(1, ignore.DefaultParameterCount);
        Assert.False(ignore.CanCallWith());
        var ex = Assert.Throws<ArgumentException>(() => ignore.CallInt64());

        Assert.Contains("cannot synthesize", ex.Message);
    }

    [Fact]
    public void Function_weak_pointer_defaults_are_rejected_by_umka_compile_boundary()
    {
        NativeTestEnvironment.RequireNativeShim();

        var compiled = UmkaRuntime.TryCompileSource("""
            fn choose*(value: weak ^int = null): int {
                return 1
            }
            """, out var runtime, out var error);

        Assert.False(compiled);
        Assert.Null(runtime);
        Assert.NotNull(error);
        Assert.Contains("Conversion to weak pointer is not allowed in constant expressions", error.Message);
    }

    [Fact]
    public void Function_accepts_variadic_parameters_as_explicit_or_expanded_dynamic_array_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn count*(values: ..int): int {
                return len(values)
            }

            fn sumTiny*(seed: int, values: ..int8): int {
                total := seed
                for _, value in values {
                    total += value
                }
                return total
            }
            """);

        runtime.Compile();
        var count = runtime.GetFunction("count");
        var parameterType = Assert.Single(count.ParameterTypes);

        Assert.Equal(1, count.ParameterCount);
        Assert.Equal(0, count.RequiredParameterCount);
        Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
        Assert.True(parameterType.IsVariadicParameterList);
        Assert.True(count.CanCallWith());
        Assert.True(count.CanCallWith(UmkaValue.FromDynamicArray(1L, 2L, 3L)));
        Assert.True(count.CanCallWith(UmkaValue.From(1L), UmkaValue.From(2L), UmkaValue.From(3L)));
        Assert.Equal(0, count.CallInt64());
        Assert.Equal(3, count.CallInt64(UmkaValue.FromDynamicArray(1L, 2L, 3L)));
        Assert.Equal(3, count.CallInt64(UmkaValue.From(1L), UmkaValue.From(2L), UmkaValue.From(3L)));

        var sumTiny = runtime.GetFunction("sumTiny");
        Assert.Equal(2, sumTiny.ParameterCount);
        Assert.Equal(1, sumTiny.RequiredParameterCount);
        Assert.True(sumTiny.ParameterTypes[1].IsVariadicParameterList);
        Assert.True(sumTiny.CanCallWith(UmkaValue.From(10L)));
        Assert.True(sumTiny.CanCallWith(UmkaValue.From(10L), UmkaValue.From((sbyte)1), UmkaValue.From((sbyte)2)));
        Assert.False(sumTiny.CanCallWith());
        Assert.False(sumTiny.CanCallWith(UmkaValue.From(10L), UmkaValue.From(128L)));
        Assert.Equal(10, sumTiny.CallInt64(UmkaValue.From(10L)));
        Assert.Equal(13, sumTiny.CallInt64(UmkaValue.From(10L), UmkaValue.From((sbyte)1), UmkaValue.From((sbyte)2)));

        var kindEx = Assert.Throws<ArgumentException>(() => count.CallInt64(UmkaValue.From("text")));
        Assert.Contains("value kind String", kindEx.Message);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sumTiny.CallInt64(UmkaValue.From(10L), UmkaValue.From(128L)));
    }

    [Fact]
    public void Function_rejects_calls_after_runtime_disposal()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();
        var answer = runtime.GetFunction("answer");
        runtime.Dispose();

        Assert.Throws<ObjectDisposedException>(() => answer.CallInt64());
    }
}
