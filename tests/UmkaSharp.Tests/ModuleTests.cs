using Xunit;

namespace UmkaSharp.Tests;

public sealed class ModuleTests
{
    [Fact]
    public void Runtime_can_import_source_modules()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "math.um"

            fn answer*(): int {
                return math::inc(41)
            }
            """);

        runtime.AddModule("math.um", """
            fn inc*(value: int): int {
                return value + 1
            }
            """);

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
    }

    [Fact]
    public void Runtime_can_import_module_source_from_file()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var moduleFile = Path.Combine(tempDir, "disk-math.um");
            File.WriteAllText(moduleFile, """
                fn inc*(value: int): int {
                    return value + 1
                }
                """);

            using var runtime = UmkaRuntime.FromSource("""
                import "math.um"

                fn answer*(): int {
                    return math::inc(41)
                }
                """);

            runtime.AddModuleFromFile("math.um", moduleFile);
            runtime.Compile();

            Assert.Equal(42, runtime.GetFunction("answer").CallInt64());
            Assert.Equal(8, runtime.GetFunction("inc", "math.um").CallInt64(UmkaValue.From(7L)));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_can_import_aliases_and_transitive_module_dependencies()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import inv = "invoice.um"

            fn total*(): int {
                return inv::gross(37)
            }
            """);

        runtime.AddModule("invoice.um", """
            import m = "math.um"

            fn gross*(value: int): int {
                return m::addFee(value)
            }
            """);

        runtime.AddModule("math.um", """
            fn addFee*(value: int): int {
                return value + 5
            }
            """);

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("total").CallInt64());
        Assert.Equal(15, runtime.GetFunction("gross", "invoice.um").CallInt64(UmkaValue.From(10L)));

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("addFee", "math.um"));

        Assert.Contains("addFee", ex.Message);
        Assert.Contains("math.um", ex.Message);
    }

    [Fact]
    public void Runtime_can_lookup_functions_from_modules_imported_by_root_source()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import m = "math.um"

            fn total*(): int {
                return m::addFee(37)
            }
            """);

        runtime.AddModule("math.um", """
            fn addFee*(value: int): int {
                return value + 5
            }
            """);

        runtime.Compile();

        var total = runtime.GetFunction("total");
        var addFee = runtime.GetFunction("addFee", "math.um");

        Assert.Equal("total", total.Name);
        Assert.Null(total.ModuleName);
        Assert.Equal("total", total.QualifiedName);
        Assert.Equal("addFee", addFee.Name);
        Assert.Equal("math.um", addFee.ModuleName);
        Assert.Equal("math.um::addFee", addFee.QualifiedName);

        Assert.Equal(42, total.CallInt64());
        Assert.Equal(12, addFee.CallInt64(UmkaValue.From(7L)));

        Assert.True(runtime.TryGetFunction("addFee", "math.um", out var optionalAddFee));
        Assert.NotNull(optionalAddFee);
        Assert.Equal("math.um", optionalAddFee.ModuleName);
        Assert.Equal("math.um::addFee", optionalAddFee.QualifiedName);
        Assert.Equal(8, optionalAddFee.CallInt64(UmkaValue.From(3L)));

        Assert.False(runtime.TryGetFunction("missing", "math.um", out var missing));
        Assert.Null(missing);
        Assert.True(runtime.IsAlive);
        Assert.Equal(42, total.CallInt64());

        var missingEx = Assert.Throws<UmkaException>(() => runtime.GetFunction("missing", "math.um"));

        Assert.Contains("missing", missingEx.Message);
        Assert.Contains("math.um", missingEx.Message);
        Assert.Equal("math.um", missingEx.Error.FileName);
        Assert.Equal("missing", missingEx.Error.FunctionName);
        Assert.Equal(2, missingEx.Error.Code);
    }

    [Fact]
    public void Runtime_exposes_registered_module_and_callback_name_snapshots()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn answer*(): int {
                host::trace()
                return 42
            }
            """);

        Assert.Empty(runtime.RegisteredModuleNames);
        Assert.Empty(runtime.RegisteredCallbackNames);

        runtime.AddModule("z.um", "fn value*(): int { return 23 }");
        runtime.AddModule("host.um", """
            fn audit*()
            fn trace*()
            """);
        runtime.AddModule("a.um", "fn value*(): int { return 19 }");
        runtime.RegisterVoid("trace", _ => { });
        runtime.Register("audit", _ => UmkaValue.Void);

        Assert.Equal(["a.um", "host.um", "z.um"], runtime.RegisteredModuleNames);
        Assert.Equal(["audit", "trace"], runtime.RegisteredCallbackNames);

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("answer").CallInt64());

        runtime.Dispose();

        Assert.Equal(["a.um", "host.um", "z.um"], runtime.RegisteredModuleNames);
        Assert.Equal(["audit", "trace"], runtime.RegisteredCallbackNames);
    }

    [Fact]
    public void Runtime_does_not_lookup_non_exported_imported_module_functions()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import m = "math.um"

            fn total*(): int {
                return m::publicAdd(37)
            }
            """);

        runtime.AddModule("math.um", """
            fn hiddenAdd(value: int): int {
                return value + 5
            }

            fn publicAdd*(value: int): int {
                return hiddenAdd(value)
            }
            """);

        runtime.Compile();

        var total = runtime.GetFunction("total");
        var publicAdd = runtime.GetFunction("publicAdd", "math.um");

        Assert.Equal(42, total.CallInt64());
        Assert.Equal(12, publicAdd.CallInt64(UmkaValue.From(7L)));

        Assert.False(runtime.TryGetFunction("hiddenAdd", "math.um", out var hiddenAdd));
        Assert.Null(hiddenAdd);
        Assert.True(runtime.IsAlive);

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("hiddenAdd", "math.um"));

        Assert.Contains("hiddenAdd", ex.Message);
        Assert.Contains("math.um", ex.Message);
        Assert.Equal("math.um", ex.Error.FileName);
        Assert.Equal("hiddenAdd", ex.Error.FunctionName);
        Assert.Equal(2, ex.Error.Code);
    }

    [Fact]
    public void Module_function_validation_messages_include_module_name()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import m = "math.um"

            fn total*(): int {
                return m::addFee(37)
            }
            """);

        runtime.AddModule("math.um", """
            fn addFee*(value: int): int {
                return value + 5
            }
            """);

        runtime.Compile();

        var addFee = runtime.GetFunction("addFee", "math.um");

        var ex = Assert.Throws<ArgumentException>(() => addFee.CallInt64(UmkaValue.From("wrong")));

        Assert.Contains("math.um::addFee", ex.Message);
        Assert.Contains("value kind String", ex.Message);
    }

    [Fact]
    public void Runtime_surfaces_missing_module_errors()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "missing.um"

            fn answer*(): int {
                return missing::answer()
            }
            """);

        var ex = Assert.Throws<UmkaException>(() => runtime.Compile());

        Assert.False(string.IsNullOrWhiteSpace(ex.Error.Message));
    }

    [Fact]
    public void Runtime_validates_module_registration_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        Assert.Throws<ArgumentException>(() => runtime.AddModule("", "fn value*(): int"));
        Assert.Throws<ArgumentException>(() => runtime.AddModule("   ", "fn value*(): int"));
        Assert.Throws<ArgumentNullException>(() => runtime.AddModule("value.um", null!));

        runtime.AddModule("math.um", "fn value*(): int { return 1 }");

        var ex = Assert.Throws<ArgumentException>(() => runtime.AddModule("math.um", "fn value*(): int { return 2 }"));
        Assert.Contains("already been added", ex.Message);
    }

    [Fact]
    public void Runtime_validates_file_module_registration_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var moduleFile = Path.Combine(tempDir, "math.um");
            var badSourceFile = Path.Combine(tempDir, "bad.um");
            File.WriteAllText(moduleFile, "fn value*(): int { return 1 }");
            File.WriteAllText(badSourceFile, "fn value*(): int\0");

            using var runtime = UmkaRuntime.FromSource("""
                fn answer*(): int {
                    return 42
                }
                """);

            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("", moduleFile));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("   ", moduleFile));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("value.um", ""));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("value.um", "   "));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("bad\0module.um", moduleFile));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("value.um", "bad\0path.um"));
            Assert.Throws<FileNotFoundException>(() => runtime.AddModuleFromFile("missing.um", Path.Combine(tempDir, "missing.um")));
            Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("bad.um", badSourceFile));

            runtime.AddModuleFromFile("math.um", moduleFile);

            var ex = Assert.Throws<ArgumentException>(() => runtime.AddModuleFromFile("math.um", moduleFile));
            Assert.Contains("already been added", ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Runtime_validates_module_name_lookup_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Compile();

        Assert.Throws<ArgumentException>(() => runtime.GetFunction("answer", ""));
        Assert.Throws<ArgumentException>(() => runtime.GetFunction("answer", "   "));
        Assert.Throws<ArgumentException>(() => runtime.TryGetFunction("answer", "", out _));
        Assert.Throws<ArgumentException>(() => runtime.TryGetFunction("answer", "   ", out _));
    }
}
