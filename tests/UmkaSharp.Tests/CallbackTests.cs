using System.Runtime.InteropServices;
using Xunit;

namespace UmkaSharp.Tests;

public sealed class CallbackTests
{
    [Fact]
    public void Runtime_rejects_duplicate_callback_names()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        runtime.Register("hook", _ => UmkaValue.Void);

        var ex = Assert.Throws<ArgumentException>(() => runtime.Register("hook", _ => UmkaValue.Void));
        var voidEx = Assert.Throws<ArgumentException>(() => runtime.RegisterVoid("hook", _ => { }));

        Assert.Contains("already been registered", ex.Message);
        Assert.Contains("already been registered", voidEx.Message);
    }

    [Fact]
    public void Runtime_can_register_void_callback_actions()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                host::add(40)
                host::add(2)
                return host::total()
            }
            """);

        runtime.AddModule("host.um", """
            fn add*(value: int)
            fn total*(): int
            """);

        long total = 0;
        var callback = runtime.RegisterVoid("add", frame =>
        {
            total += frame.GetInt64(0);
        });
        runtime.Register("total", _ => UmkaValue.From(total));

        runtime.Compile();

        Assert.Equal("add", callback.Name);
        Assert.Equal(42, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Void_callback_actions_capture_managed_failures()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*() {
                host::fail()
            }
            """);

        runtime.AddModule("host.um", "fn fail*()");
        var callback = runtime.RegisterVoid("fail", _ => throw new InvalidOperationException("boom"));

        runtime.Compile();

        Assert.Null(runtime.LastCallbackException);

        var ex = Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallVoid());
        var callbackEx = Assert.IsType<InvalidOperationException>(callback.LastException);
        Assert.Same(callbackEx, ex.InnerException);
        Assert.Same(callbackEx, runtime.LastCallbackException);
        Assert.Equal("boom", callbackEx.Message);
    }

    [Fact]
    public void Registered_callback_exposes_name_and_runtime_owned_lifecycle()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var callback = runtime.Register("hook", _ => UmkaValue.Void);

        Assert.Equal("hook", callback.Name);
        Assert.False(callback.IsDisposed);
        Assert.Null(callback.LastException);

        runtime.Dispose();

        Assert.Equal("hook", callback.Name);
        Assert.True(callback.IsDisposed);
        Assert.Null(callback.LastException);
    }

    [Fact]
    public void Runtime_can_lookup_registered_callbacks_by_name()
    {
        NativeTestEnvironment.RequireNativeShim();

        var runtime = UmkaRuntime.FromSource("""
            fn answer*(): int {
                return 42
            }
            """);

        var hook = runtime.Register("hook", _ => UmkaValue.Void);
        var notify = runtime.RegisterVoid("notify", _ => { });

        Assert.Same(hook, runtime.GetCallback("hook"));
        Assert.True(runtime.TryGetCallback("notify", out var optionalNotify));
        Assert.Same(notify, optionalNotify);
        Assert.False(runtime.TryGetCallback("missing", out var missing));
        Assert.Null(missing);

        var ex = Assert.Throws<KeyNotFoundException>(() => runtime.GetCallback("missing"));
        Assert.Contains("missing", ex.Message);

        Assert.Throws<ArgumentException>(() => runtime.GetCallback(""));
        Assert.Throws<ArgumentException>(() => runtime.TryGetCallback("bad\0callback", out _));

        runtime.Dispose();

        Assert.Same(hook, runtime.GetCallback("hook"));
        Assert.True(runtime.GetCallback("hook").IsDisposed);
        Assert.True(runtime.TryGetCallback("notify", out var disposedNotify));
        Assert.Same(notify, disposedNotify);
        Assert.True(disposedNotify.IsDisposed);
    }

    [Fact]
    public void Callback_frame_exposes_argument_and_result_type_metadata()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): str {
                return host::describe(40, uint(2), 3.5, true, "Ada")
            }
            """);

        runtime.AddModule("host.um", """
            fn describe*(i: int, u: uint, r: real, b: bool, s: str): str
            """);

        runtime.Register("describe", frame =>
        {
            Assert.Equal(5, frame.ParameterCount);
            Assert.IsNotType<UmkaTypeInfo[]>(frame.ParameterTypes);
            Assert.Throws<NotSupportedException>(() =>
                ((System.Collections.Generic.IList<UmkaTypeInfo>)frame.ParameterTypes)[0] =
                    new UmkaTypeInfo(UmkaTypeKind.Unknown, "mutated"));
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ParameterTypes[0].Kind);
            Assert.Equal(UmkaTypeKind.UnsignedInteger, frame.ParameterTypes[1].Kind);
            Assert.Equal(UmkaTypeKind.Real, frame.ParameterTypes[2].Kind);
            Assert.Equal(UmkaTypeKind.Boolean, frame.ParameterTypes[3].Kind);
            Assert.Equal(UmkaTypeKind.String, frame.ParameterTypes[4].Kind);
            Assert.Equal(UmkaTypeKind.String, frame.ResultType.Kind);

            return UmkaValue.From(
                $"{frame.GetString(4)}:{frame.GetInt64(0) + (long)frame.GetUInt64(1)}:{frame.GetDouble(2):0.0}:{frame.GetBoolean(3)}");
        });

        runtime.Compile();

        Assert.Equal("Ada:42:3.5:True", runtime.GetFunction("run").CallString());
    }

    [Fact]
    public void Callback_frame_rejects_use_after_callback_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect(42)
            }
            """);

        runtime.AddModule("host.um", "fn inspect*(value: int): int");
        UmkaCallFrame capturedFrame = default;
        runtime.Register("inspect", frame =>
        {
            capturedFrame = frame;

            Assert.Equal(1, frame.ParameterCount);
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ParameterTypes[0].Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ResultType.Kind);
            Assert.Equal(42, frame.GetInt64(0));

            return UmkaValue.From(43);
        });

        runtime.Compile();

        Assert.Equal(43, runtime.GetFunction("run").CallInt64());
        var countEx = Assert.Throws<InvalidOperationException>(() => _ = capturedFrame.ParameterCount);
        var parameterTypesEx = Assert.Throws<InvalidOperationException>(() => _ = capturedFrame.ParameterTypes);
        var readerEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.GetInt64(0));
        var dynamicReaderEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.GetValue(0));
        var tryReaderEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.TryGetScalar<int>(0, out _));
        var tryDynamicReaderEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.TryGetValue(0, out _));
        var tryStructReaderEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.TryGetStruct<IntPair>(0, out _));
        var tryArrayReaderEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.TryGetArray<long>(0, 1, out _));
        var canReadValueEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.CanReadArgumentAsValue(0));
        var canReadScalarEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.CanReadArgumentAsScalar<int>(0));
        var canReadStructEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.CanReadArgumentAsStruct<IntPair>(0));
        var canReadArrayEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.CanReadArgumentAsArray<long>(0, 1));
        var resultEx = Assert.Throws<InvalidOperationException>(() => _ = capturedFrame.ResultType);
        var canReturnEx = Assert.Throws<InvalidOperationException>(() => capturedFrame.CanReturn(UmkaValue.From(43)));

        Assert.Contains("no longer active", countEx.Message);
        Assert.Contains("no longer active", parameterTypesEx.Message);
        Assert.Contains("no longer active", readerEx.Message);
        Assert.Contains("no longer active", dynamicReaderEx.Message);
        Assert.Contains("no longer active", tryReaderEx.Message);
        Assert.Contains("no longer active", tryDynamicReaderEx.Message);
        Assert.Contains("no longer active", tryStructReaderEx.Message);
        Assert.Contains("no longer active", tryArrayReaderEx.Message);
        Assert.Contains("no longer active", canReadValueEx.Message);
        Assert.Contains("no longer active", canReadScalarEx.Message);
        Assert.Contains("no longer active", canReadStructEx.Message);
        Assert.Contains("no longer active", canReadArrayEx.Message);
        Assert.Contains("no longer active", resultEx.Message);
        Assert.Contains("no longer active", canReturnEx.Message);
    }

    [Fact]
    public void Callback_frame_metadata_snapshots_can_be_retained_after_callback_returns()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): real {
                return host::score(40, 2.5)
            }
            """);

        runtime.AddModule("host.um", "fn score*(base: int, bonus: real): real");

        IReadOnlyList<UmkaTypeInfo>? capturedParameterTypes = null;
        UmkaTypeInfo? capturedResultType = null;
        runtime.Register("score", frame =>
        {
            capturedParameterTypes = frame.ParameterTypes;
            capturedResultType = frame.ResultType;

            return UmkaValue.From(frame.GetInt64(0) + frame.GetDouble(1));
        });

        runtime.Compile();

        Assert.Equal(42.5, runtime.GetFunction("run").CallDouble());
        Assert.NotNull(capturedParameterTypes);
        Assert.NotNull(capturedResultType);
        Assert.Equal(2, capturedParameterTypes.Count);
        Assert.Equal(UmkaTypeKind.SignedInteger, capturedParameterTypes[0].Kind);
        Assert.Equal(UmkaTypeKind.Real, capturedParameterTypes[1].Kind);
        Assert.Equal(UmkaTypeKind.Real, capturedResultType.Kind);
    }

    [Fact]
    public void Default_callback_frame_rejects_reads()
    {
        var frame = default(UmkaCallFrame);

        var ex = Assert.Throws<InvalidOperationException>(() => _ = frame.ParameterCount);
        var dynamicReaderEx = Assert.Throws<InvalidOperationException>(() => frame.GetValue(0));
        var tryDynamicReaderEx = Assert.Throws<InvalidOperationException>(() => frame.TryGetValue(0, out _));
        var canReadValueEx = Assert.Throws<InvalidOperationException>(() => frame.CanReadArgumentAsValue(0));
        var canReturnEx = Assert.Throws<InvalidOperationException>(() => frame.CanReturn(UmkaValue.Void));

        Assert.Contains("not initialized", ex.Message);
        Assert.Contains("not initialized", dynamicReaderEx.Message);
        Assert.Contains("not initialized", tryDynamicReaderEx.Message);
        Assert.Contains("not initialized", canReadValueEx.Message);
        Assert.Contains("not initialized", canReturnEx.Message);
    }

    [Fact]
    public void Callback_frame_reads_narrow_scalar_arguments_with_typed_helpers()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): bool {
                return host::inspect(
                    int8(-8),
                    int16(-1600),
                    int32(-32000),
                    uint8(200),
                    uint16(65000),
                    uint32(4000000000),
                    'A',
                    real32(1.25))
            }
            """);

        runtime.AddModule("host.um", """
            fn inspect*(
                i8: int8,
                i16: int16,
                i32: int32,
                u8: uint8,
                u16: uint16,
                u32: uint32,
                c: char,
                r32: real32): bool
            """);

        runtime.Register("inspect", frame =>
        {
            Assert.Equal((sbyte)-8, frame.GetSByte(0));
            Assert.Equal((short)-1600, frame.GetInt16(1));
            Assert.Equal(-32000, frame.GetInt32(2));
            Assert.Equal((byte)200, frame.GetByte(3));
            Assert.Equal((ushort)65000, frame.GetUInt16(4));
            Assert.Equal(4_000_000_000U, frame.GetUInt32(5));
            Assert.Equal('A', frame.GetChar(6));
            Assert.Equal(1.25f, frame.GetSingle(7));

            return UmkaValue.From(true);
        });

        runtime.Compile();

        Assert.True(runtime.GetFunction("run").CallBoolean());
    }

    [Fact]
    public void Callback_marshals_enum_arguments_and_results_as_underlying_integer_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Color = enum {red; green; blue}

            type Mode = enum (uint8) {
                draw = 74
                select
                remove = 8
                edit
            }

            fn choose*(color: Color, current: Mode): Mode

            fn run*(): Mode {
                return choose(.green, .remove)
            }
            """);

        runtime.Register("choose", frame =>
        {
            Assert.Equal(2, frame.ParameterCount);
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ParameterTypes[0].Kind);
            Assert.Equal(UmkaTypeKind.UnsignedInteger, frame.ParameterTypes[1].Kind);
            Assert.Equal(UmkaTypeKind.UnsignedInteger, frame.ResultType.Kind);
            Assert.Equal(1, frame.GetInt64(0));
            Assert.Equal((byte)8, frame.GetByte(1));
            Assert.Equal(HostColor.Green, frame.GetEnum<HostColor>(0));
            Assert.Equal(HostMode.Remove, frame.GetEnum<HostMode>(1));
            Assert.True(frame.TryGetEnum<HostColor>(0, out var tryColor));
            Assert.Equal(HostColor.Green, tryColor);
            Assert.True(frame.TryGetEnum<HostMode>(1, out var tryMode));
            Assert.Equal(HostMode.Remove, tryMode);
            Assert.False(frame.TryGetEnum<HostColor>(1, out var wrongStorageColor));
            Assert.Equal(default, wrongStorageColor);
            Assert.Throws<InvalidOperationException>(() => frame.GetEnum<HostColor>(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetEnum<HostColor>(99, out _));

            return UmkaValue.FromEnum(HostMode.Select);
        });

        runtime.Compile();

        Assert.Equal((byte)75, runtime.GetFunction("run").CallByte());
        Assert.Equal(HostMode.Select, runtime.GetFunction("run").CallEnum<HostMode>());
    }

    [Fact]
    public void Callback_frame_reads_supported_scalar_arguments_with_generic_helper()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            type Color = enum {red; green; blue}

            type Mode = enum (uint8) {
                draw = 74
                select
                remove = 8
                edit
            }

            fn inspect*(i: int, u: uint, r: real, b: bool, c: char, s: str, p: ^void, color: Color, current: Mode): int

            fn run*(): int {
                return inspect(-42, uint(42), 12.25, true, 'A', "value", null, .green, .select)
            }
            """);

        runtime.Register("inspect", frame =>
        {
            Assert.Equal(-42, frame.GetScalar<int>(0));
            Assert.Equal(42UL, frame.GetScalar<ulong>(1));
            Assert.Equal(12.25, frame.GetScalar<double>(2));
            Assert.True(frame.GetScalar<bool>(3));
            Assert.Equal('A', frame.GetScalar<char>(4));
            Assert.Equal("value", frame.GetScalar<string>(5));
            Assert.Equal(IntPtr.Zero, frame.GetScalar<IntPtr>(6));
            Assert.Equal(HostColor.Green, frame.GetScalar<HostColor>(7));
            Assert.Equal(HostMode.Select, frame.GetScalar<HostMode>(8));
            Assert.Equal(-42, frame.GetScalar<UmkaValue>(0).AsInt64());
            Assert.Equal(-42, frame.GetValue(0).AsInt64());
            Assert.Equal(42UL, frame.GetValue(1).AsUInt64());
            Assert.Equal(12.25, frame.GetValue(2).AsDouble());
            Assert.True(frame.GetValue(3).AsBoolean());
            Assert.Equal('A', frame.GetValue(4).AsChar());
            Assert.Equal("value", frame.GetValue(5).AsString());
            Assert.Equal(IntPtr.Zero, frame.GetValue(6).AsPointer());

            Assert.True(frame.CanReadArgumentAsValue(0));
            Assert.True(frame.CanReadArgumentAsValue(5));
            Assert.True(frame.CanReadArgumentAsScalar<int>(0));
            Assert.True(frame.CanReadArgumentAsScalar<string>(5));
            Assert.True(frame.CanReadArgumentAsScalar<HostColor>(7));
            Assert.True(frame.CanReadArgumentAsScalar<HostMode>(8));
            Assert.False(frame.CanReadArgumentAsScalar<string>(0));
            Assert.False(frame.CanReadArgumentAsScalar<HostColor>(8));
            Assert.False(frame.CanReadArgumentAsScalar<IntPair>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.CanReadArgumentAsValue(99));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.CanReadArgumentAsScalar<int>(99));

            Assert.True(frame.TryGetScalar<int>(0, out var signed));
            Assert.Equal(-42, signed);
            Assert.True(frame.TryGetScalar<string>(5, out var text));
            Assert.Equal("value", text);
            Assert.True(frame.TryGetScalar<HostColor>(7, out var color));
            Assert.Equal(HostColor.Green, color);
            Assert.True(frame.TryGetScalar<UmkaValue>(0, out var dynamicValue));
            Assert.Equal(-42, dynamicValue.AsInt64());
            Assert.True(frame.TryGetValue(5, out var dynamicText));
            Assert.Equal("value", dynamicText.AsString());

            Assert.Throws<InvalidOperationException>(() => frame.GetScalar<string>(0));
            Assert.Throws<InvalidOperationException>(() => frame.GetScalar<HostColor>(8));
            Assert.Throws<NotSupportedException>(() => frame.GetScalar<IntPair>(0));
            Assert.False(frame.TryGetScalar<string>(0, out var wrongKindText));
            Assert.Null(wrongKindText);
            Assert.False(frame.TryGetScalar<HostColor>(8, out var wrongStorageColor));
            Assert.Equal(default, wrongStorageColor);
            Assert.False(frame.TryGetScalar<IntPair>(0, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetScalar<int>(99, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetValue(99, out _));

            return UmkaValue.FromScalar(42);
        });

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_single_reader_rejects_real_values_outside_single_range()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect(1.0e100)
            }
            """);

        runtime.AddModule("host.um", "fn inspect*(value: real): int");
        var callback = runtime.Register("inspect", frame =>
        {
            _ = frame.GetSingle(0);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<OverflowException>(callback.LastException);
    }

    [Fact]
    public void Callback_reader_rejects_type_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect("value")
            }
            """);

        runtime.AddModule("host.um", "fn inspect*(value: str): int");
        var callback = runtime.Register("inspect", frame =>
        {
            _ = frame.GetInt64(0);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Callback_reader_rejects_invalid_argument_indexes()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect(42)
            }
            """);

        runtime.AddModule("host.um", "fn inspect*(value: int): int");
        var callback = runtime.Register("inspect", frame =>
        {
            _ = frame.GetInt64(1);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<ArgumentOutOfRangeException>(callback.LastException);
    }

    [Fact]
    public void Callback_frame_copies_reference_free_map_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspectScores(map[int]int{1: 10, 2: 20, 3: 30})
            }
            """);

        runtime.AddModule("host.um", "fn inspectScores*(value: map[int]int): int");
        runtime.Register("inspectScores", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapKeyKind);
            Assert.Equal("int", parameterType.MapKeyTypeName);
            Assert.Equal(8, parameterType.MapKeyNativeSize);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapValueKind);
            Assert.Equal("int", parameterType.MapValueTypeName);
            Assert.Equal(8, parameterType.MapValueNativeSize);
            Assert.False(parameterType.MapValueHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsMap<long, long>(0));
            Assert.False(frame.CanReadArgumentAsMap<int, long>(0));

            var values = frame.GetMap<long, long>(0);
            Assert.Equal(3, values.Count);
            Assert.Equal(10, values[1]);
            Assert.Equal(20, values[2]);
            Assert.Equal(30, values[3]);
            Assert.True(frame.TryGetMap<long, long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(30, tryValues[3]);
            Assert.False(frame.TryGetMap<int, long>(0, out var wrongKeySize));
            Assert.Null(wrongKeySize);

            var total = 0L;
            foreach (var value in values.Values)
                total += value;
            return UmkaValue.From(total);
        });

        runtime.Compile();

        Assert.Equal(60, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_rejects_reference_bearing_map_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspectAnyScores(map[int]any{1: 42})
            }
            """);

        runtime.AddModule("host.um", "fn inspectAnyScores*(value: map[int]any): int");
        runtime.Register("inspectAnyScores", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapKeyKind);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.Interface, parameterType.MapValueKind);
            Assert.True(parameterType.MapValueHasReferences);
            Assert.True(parameterType.IsDeferred);
            Assert.False(frame.CanReadArgumentAsMap<long, IntPtr>(0));
            Assert.False(frame.TryGetMap<long, IntPtr>(0, out var values));
            Assert.Null(values);
            var ex = Assert.Throws<InvalidOperationException>(() => frame.GetMap<long, IntPtr>(0));
            Assert.Contains("value type", ex.Message);
            Assert.Contains("contains Umka-managed references", ex.Message);

            return UmkaValue.From(1);
        });

        runtime.Compile();

        Assert.Equal(1, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_copies_maps_with_opaque_weak_pointer_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            weakTarget := 42

            fn run*(): int {
                expected := weak ^int(&weakTarget)
                return host::inspectWeakKeyScores(map[weak ^int]int{expected: 7}, expected) +
                    host::inspectWeakValueScores(map[int]weak ^int{1: expected}, expected)
            }
            """);

        runtime.AddModule("host.um", """
            fn inspectWeakKeyScores*(value: map[weak ^int]int, expected: weak ^int): int
            fn inspectWeakValueScores*(value: map[int]weak ^int, expected: weak ^int): int
            """);

        runtime.Register("inspectWeakKeyScores", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.WeakPointer, parameterType.MapKeyKind);
            Assert.Equal(8, parameterType.MapKeyNativeSize);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapValueKind);
            Assert.Equal(8, parameterType.MapValueNativeSize);
            Assert.False(parameterType.MapValueHasReferences);
            Assert.True(frame.CanReadArgumentAsMap<ulong, long>(0));

            var expected = frame.GetWeakPointer(1);
            var values = frame.GetMap<ulong, long>(0);
            Assert.Equal(7, values[expected]);
            Assert.True(frame.TryGetMap<ulong, long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(7, tryValues[expected]);

            return UmkaValue.From(values[expected]);
        });

        runtime.Register("inspectWeakValueScores", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapKeyKind);
            Assert.Equal(8, parameterType.MapKeyNativeSize);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.WeakPointer, parameterType.MapValueKind);
            Assert.Equal(8, parameterType.MapValueNativeSize);
            Assert.False(parameterType.MapValueHasReferences);
            Assert.True(frame.CanReadArgumentAsMap<long, ulong>(0));

            var expected = frame.GetWeakPointer(1);
            var values = frame.GetMap<long, ulong>(0);
            Assert.Equal(expected, values[1]);
            Assert.True(frame.TryGetMap<long, ulong>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(expected, tryValues[1]);

            return UmkaValue.From(values[1] == expected ? 11 : 0);
        });

        runtime.Compile();

        Assert.Equal(18, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_copies_string_map_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspectTextScores(map[str]int{"answer": 42}) +
                    host::inspectLabels(map[int]str{1: "one", 2: "two"}) +
                    host::inspectAliases(map[str]str{"a": "alpha", "b": "beta"})
            }
            """);

        runtime.AddModule("host.um", """
            fn inspectTextScores*(value: map[str]int): int
            fn inspectLabels*(value: map[int]str): int
            fn inspectAliases*(value: map[str]str): int
            """);

        runtime.Register("inspectTextScores", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapKeyKind);
            Assert.True(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapValueKind);
            Assert.False(parameterType.MapValueHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsStringKeyMap<long>(0));
            Assert.False(frame.CanReadArgumentAsStringKeyMap<int>(0));
            Assert.False(frame.CanReadArgumentAsMap<IntPtr, long>(0));

            var values = frame.GetStringKeyMap<long>(0);
            Assert.Equal(42, values["answer"]);
            Assert.True(frame.TryGetStringKeyMap<long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(42, tryValues["answer"]);
            Assert.False(frame.TryGetStringKeyMap<int>(0, out var wrongValueSize));
            Assert.Null(wrongValueSize);

            return UmkaValue.From(values["answer"]);
        });

        runtime.Register("inspectLabels", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapKeyKind);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapValueKind);
            Assert.True(parameterType.MapValueHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsStringValueMap<long>(0));
            Assert.False(frame.CanReadArgumentAsMap<long, IntPtr>(0));

            var values = frame.GetStringValueMap<long>(0);
            Assert.Equal("one", values[1]);
            Assert.Equal("two", values[2]);
            Assert.True(frame.TryGetStringValueMap<long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal("two", tryValues[2]);

            return UmkaValue.From(values.Values.Sum(value => value?.Length ?? 0));
        });

        runtime.Register("inspectAliases", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapKeyKind);
            Assert.True(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapValueKind);
            Assert.True(parameterType.MapValueHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsStringMap(0));
            Assert.False(frame.CanReadArgumentAsStringKeyMap<long>(0));

            var values = frame.GetStringMap(0);
            Assert.Equal("alpha", values["a"]);
            Assert.Equal("beta", values["b"]);
            Assert.True(frame.TryGetStringMap(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal("beta", tryValues["b"]);

            return UmkaValue.From(values.Values.Sum(value => value?.Length ?? 0));
        });

        runtime.Compile();

        Assert.Equal(57, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_copies_dynamic_array_value_map_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspectRows(map[int][]int{1: []int{1, 2}, 2: []int{}, 3: []int{3, 4, 5}}) +
                    host::inspectNamedRows(map[str][]int{"left": []int{10, 11}, "empty": []int{}}) +
                    host::inspectTextRows(map[int][]str{1: []str{"a", "b"}, 2: []str{}}) +
                    host::inspectNamedTextRows(map[str][]str{"left": []str{"x", "y"}, "empty": []str{}}) +
                    host::inspectAnyRows(map[int][]any{1: []any{42}})
            }
            """);

        runtime.AddModule("host.um", """
            fn inspectRows*(value: map[int][]int): int
            fn inspectNamedRows*(value: map[str][]int): int
            fn inspectTextRows*(value: map[int][]str): int
            fn inspectNamedTextRows*(value: map[str][]str): int
            fn inspectAnyRows*(value: map[int][]any): int
            """);

        runtime.Register("inspectRows", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapKeyKind);
            Assert.Equal(8, parameterType.MapKeyNativeSize);
            Assert.False(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.MapValueKind);
            Assert.Equal("[]int", parameterType.MapValueTypeName);
            Assert.Equal(IntPtr.Size * 3, parameterType.MapValueNativeSize);
            Assert.True(parameterType.MapValueHasReferences);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapValueElementKind);
            Assert.Equal("int", parameterType.MapValueElementTypeName);
            Assert.Equal(8, parameterType.MapValueElementNativeSize);
            Assert.False(parameterType.MapValueElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsDynamicArrayValueMap<long, long>(0));
            Assert.False(frame.CanReadArgumentAsDynamicArrayValueMap<int, long>(0));
            Assert.False(frame.CanReadArgumentAsMap<long, IntPtr>(0));

            var values = frame.GetDynamicArrayValueMap<long, long>(0);
            Assert.Equal([1L, 2L], values[1]);
            Assert.Empty(values[2]);
            Assert.Equal([3L, 4L, 5L], values[3]);
            Assert.True(frame.TryGetDynamicArrayValueMap<long, long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal([3L, 4L, 5L], tryValues[3]);
            Assert.False(frame.TryGetDynamicArrayValueMap<int, long>(0, out var wrongKeySize));
            Assert.Null(wrongKeySize);

            return UmkaValue.From(values.Values.Sum(row => row.Sum()));
        });

        runtime.Register("inspectNamedRows", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapKeyKind);
            Assert.True(parameterType.MapKeyHasReferences);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.MapValueKind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.MapValueElementKind);
            Assert.False(parameterType.MapValueElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsStringKeyDynamicArrayValueMap<long>(0));
            Assert.False(frame.CanReadArgumentAsStringKeyDynamicArrayValueMap<int>(0));

            var values = frame.GetStringKeyDynamicArrayValueMap<long>(0);
            Assert.Equal([10L, 11L], values["left"]);
            Assert.Empty(values["empty"]);
            Assert.True(frame.TryGetStringKeyDynamicArrayValueMap<long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal([10L, 11L], tryValues["left"]);
            Assert.False(frame.TryGetStringKeyDynamicArrayValueMap<int>(0, out var wrongElementSize));
            Assert.Null(wrongElementSize);

            return UmkaValue.From(values["left"].Sum());
        });

        runtime.Register("inspectTextRows", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.MapValueKind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapValueElementKind);
            Assert.Equal("str", parameterType.MapValueElementTypeName);
            Assert.True(parameterType.MapValueElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsStringArrayValueMap<long>(0));
            Assert.False(frame.CanReadArgumentAsStringArrayValueMap<int>(0));
            Assert.False(frame.CanReadArgumentAsDynamicArrayValueMap<long, IntPtr>(0));

            var values = frame.GetStringArrayValueMap<long>(0);
            Assert.Collection(
                values[1],
                value => Assert.Equal("a", value),
                value => Assert.Equal("b", value));
            Assert.Empty(values[2]);
            Assert.True(frame.TryGetStringArrayValueMap<long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Collection(
                tryValues[1],
                value => Assert.Equal("a", value),
                value => Assert.Equal("b", value));
            Assert.False(frame.TryGetStringArrayValueMap<int>(0, out var wrongKeySize));
            Assert.Null(wrongKeySize);

            return UmkaValue.From(values[1].Sum(value => value?.Length ?? 0));
        });

        runtime.Register("inspectNamedTextRows", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapKeyKind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.MapValueKind);
            Assert.Equal(UmkaTypeKind.String, parameterType.MapValueElementKind);
            Assert.False(parameterType.IsDeferred);
            Assert.False(frame.CanReadArgumentAsStringKeyDynamicArrayValueMap<IntPtr>(0));
            Assert.True(frame.CanReadArgumentAsStringKeyStringArrayValueMap(0));

            var values = frame.GetStringKeyStringArrayValueMap(0);
            Assert.Collection(
                values["left"],
                value => Assert.Equal("x", value),
                value => Assert.Equal("y", value));
            Assert.Empty(values["empty"]);
            Assert.True(frame.TryGetStringKeyStringArrayValueMap(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Collection(
                tryValues["left"],
                value => Assert.Equal("x", value),
                value => Assert.Equal("y", value));

            return UmkaValue.From(values["left"].Sum(value => value?.Length ?? 0));
        });

        runtime.Register("inspectAnyRows", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Map, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.MapValueKind);
            Assert.Equal(UmkaTypeKind.Interface, parameterType.MapValueElementKind);
            Assert.True(parameterType.MapValueElementHasReferences);
            Assert.True(parameterType.IsDeferred);
            Assert.False(frame.CanReadArgumentAsStringArrayValueMap<long>(0));
            Assert.False(frame.TryGetStringArrayValueMap<long>(0, out var values));
            Assert.Null(values);
            Assert.Throws<InvalidOperationException>(() => frame.GetStringArrayValueMap<long>(0));

            return UmkaValue.From(1);
        });

        runtime.Compile();

        Assert.Equal(41, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_exposes_unsupported_argument_metadata_and_rejects_readers()
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

            fn run*(): int {
                value := new(int, 42)
                var fiberValue: fiber
                var speaker: host::Speaker = Dog{woofCount: 0}
                return host::inspectDynamicArray([]int{1, 2, 3}) +
                    host::inspectMap(map[int][]str{1: []str{"value"}}) +
                    host::inspectInterface(speaker) +
                    host::inspectAny(42) +
                    host::inspectClosure(fn (): int {
                        return 42
                    }) +
                    host::inspectWeak(weak ^int(value)) +
                    host::inspectFiber(fiberValue)
            }
            """);

        runtime.AddModule("host.um", """
            type Speaker* = interface {
                speak(): str
            }

            type IntFn* = fn (): int
            fn inspectDynamicArray*(value: []int): int
            fn inspectMap*(value: map[int][]str): int
            fn inspectInterface*(value: Speaker): int
            fn inspectAny*(value: any): int
            fn inspectClosure*(value: IntFn): int
            fn inspectWeak*(value: weak ^int): int
            fn inspectFiber*(value: fiber): int
            """);

        runtime.Register("inspectDynamicArray", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.ElementKind);
            Assert.Equal("int", parameterType.ElementTypeName);
            Assert.Equal(8, parameterType.ElementNativeSize);
            Assert.False(parameterType.ElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsDynamicArray<long>(0));
            Assert.False(frame.CanReadArgumentAsDynamicArray<int>(0));
            Assert.Collection(
                frame.GetDynamicArray<long>(0),
                value => Assert.Equal(1L, value),
                value => Assert.Equal(2L, value),
                value => Assert.Equal(3L, value));
            Assert.True(frame.TryGetDynamicArray<long>(0, out var values));
            Assert.NotNull(values);
            Assert.Equal([1L, 2L, 3L], values);
            Assert.False(frame.TryGetDynamicArray<int>(0, out var wrongElementSize));
            Assert.Null(wrongElementSize);

            return UmkaValue.From(1);
        });
        runtime.Register("inspectMap", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Map));
        runtime.Register("inspectInterface", frame =>
        {
            AssertInterfaceMetadata(Assert.Single(frame.ParameterTypes), expectedItemCount: 3);
            return AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Interface);
        });
        runtime.Register("inspectAny", frame =>
        {
            AssertAnyMetadata(Assert.Single(frame.ParameterTypes));
            return AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Interface, "interface");
        });
        runtime.Register("inspectClosure", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.Closure, parameterType.Kind);
            Assert.True(parameterType.HasReferences);
            Assert.True(parameterType.IsDeferred);
            Assert.True(parameterType.NativeSize >= IntPtr.Size * 2);

            return AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Closure);
        });
        runtime.Register("inspectWeak", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.WeakPointer, parameterType.Kind);
            Assert.False(parameterType.HasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(parameterType.CanReadAsValue());
            Assert.True(parameterType.CanReadAsWeakPointer());
            Assert.True(frame.CanReadArgumentAsValue(0));
            Assert.True(frame.CanReadArgumentAsScalar<UmkaValue>(0));
            Assert.True(frame.CanReadArgumentAsWeakPointer(0));
            Assert.False(frame.CanReadArgumentAsScalar<ulong>(0));

            var handle = frame.GetWeakPointer(0);
            Assert.NotEqual(0UL, handle);
            Assert.True(frame.TryGetWeakPointer(0, out var tryHandle));
            Assert.Equal(handle, tryHandle);

            var dynamicValue = frame.GetValue(0);
            Assert.Equal(UmkaValueKind.WeakPointer, dynamicValue.Kind);
            Assert.Equal(handle, dynamicValue.AsWeakPointer());
            Assert.True(frame.TryGetValue(0, out var tryValue));
            Assert.Equal(handle, tryValue.AsWeakPointer());
            Assert.True(frame.TryGetScalar<UmkaValue>(0, out var scalarValue));
            Assert.Equal(handle, scalarValue.AsWeakPointer());
            Assert.False(frame.TryGetScalar<ulong>(0, out _));

            return UmkaValue.From(1);
        });
        runtime.Register("inspectFiber", frame =>
        {
            AssertFiberMetadata(Assert.Single(frame.ParameterTypes));
            return AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Fiber);
        });

        runtime.Compile();

        Assert.Equal(7, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_can_read_null_string_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): bool {
                var value: str
                return host::isNull(value)
            }
            """);

        runtime.AddModule("host.um", "fn isNull*(value: str): bool");
        runtime.Register("isNull", frame =>
        {
            Assert.Equal(UmkaTypeKind.String, frame.ParameterTypes[0].Kind);
            Assert.Null(frame.GetString(0));
            return UmkaValue.From(true);
        });

        runtime.Compile();

        Assert.True(runtime.GetFunction("run").CallBoolean());
    }

    [Fact]
    public void Callback_can_read_pointer_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): bool {
                value := 42
                return host::isNonNull(&value)
            }
            """);

        runtime.AddModule("host.um", "fn isNonNull*(value: ^int): bool");
        runtime.Register("isNonNull", frame =>
        {
            Assert.Equal(UmkaTypeKind.Pointer, frame.ParameterTypes[0].Kind);
            Assert.NotEqual(IntPtr.Zero, frame.GetPointer(0));
            return UmkaValue.From(true);
        });

        runtime.Compile();

        Assert.True(runtime.GetFunction("run").CallBoolean());
    }

    [Fact]
    public void Callback_can_read_fixed_layout_struct_and_static_array_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): real {
                return host::inspect(host::Point{3.0, 4.0}, [3]int{10, 14, 18})
            }
            """);

        runtime.AddModule("host.um", """
            type Point* = struct {x, y: real}
            fn inspect*(point: Point, values: [3]int): real
            """);

        runtime.Register("inspect", frame =>
        {
            Assert.Equal(2, frame.ParameterCount);
            Assert.Equal(UmkaTypeKind.Struct, frame.ParameterTypes[0].Kind);
            Assert.Equal(Marshal.SizeOf<RealPoint>(), frame.ParameterTypes[0].NativeSize);
            Assert.False(frame.ParameterTypes[0].HasReferences);
            Assert.Equal(UmkaTypeKind.StaticArray, frame.ParameterTypes[1].Kind);
            Assert.Equal(3 * Marshal.SizeOf<long>(), frame.ParameterTypes[1].NativeSize);
            Assert.Equal(3, frame.ParameterTypes[1].ItemCount);
            Assert.False(frame.ParameterTypes[1].HasReferences);

            var point = frame.GetStruct<RealPoint>(0);
            var values = frame.GetArray<long>(1, 3);
            Assert.True(frame.CanReadArgumentAsStruct<RealPoint>(0));
            Assert.True(frame.CanReadArgumentAsArray<long>(1, 3));

            Assert.True(frame.TryGetStruct<RealPoint>(0, out var tryPoint));
            Assert.Equal(point.X, tryPoint.X);
            Assert.Equal(point.Y, tryPoint.Y);

            Assert.True(frame.TryGetArray<long>(1, 3, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(values, tryValues);

            Assert.False(frame.TryGetStruct<NativeStringBox>(0, out var wrongStructSize));
            Assert.Equal(default, wrongStructSize);
            Assert.False(frame.CanReadArgumentAsStruct<NativeStringBox>(0));
            Assert.False(frame.TryGetStruct<ManagedStringBox>(0, out var managedStruct));
            Assert.Equal(default, managedStruct);
            Assert.False(frame.CanReadArgumentAsStruct<ManagedStringBox>(0));
            Assert.False(frame.TryGetStruct<RealPoint>(1, out var arrayAsStruct));
            Assert.Equal(default, arrayAsStruct);
            Assert.False(frame.CanReadArgumentAsStruct<RealPoint>(1));
            Assert.False(frame.TryGetArray<long>(1, 2, out var wrongLength));
            Assert.Null(wrongLength);
            Assert.False(frame.CanReadArgumentAsArray<long>(1, 2));
            Assert.False(frame.TryGetArray<int>(1, 3, out var wrongElementSize));
            Assert.Null(wrongElementSize);
            Assert.False(frame.CanReadArgumentAsArray<int>(1, 3));
            Assert.False(frame.TryGetArray<ManagedStringBox>(1, 3, out var managedArray));
            Assert.Null(managedArray);
            Assert.False(frame.CanReadArgumentAsArray<ManagedStringBox>(1, 3));
            Assert.False(frame.TryGetArray<long>(0, 3, out var structAsArray));
            Assert.Null(structAsArray);
            Assert.False(frame.CanReadArgumentAsArray<long>(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetStruct<RealPoint>(99, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetArray<long>(99, 3, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.TryGetArray<long>(1, -1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.CanReadArgumentAsStruct<RealPoint>(99));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.CanReadArgumentAsArray<long>(99, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => frame.CanReadArgumentAsArray<long>(1, -1));

            var lengthEx = Assert.Throws<InvalidOperationException>(() => frame.GetArray<long>(1, 2));

            Assert.Contains("3 item", lengthEx.Message);

            return UmkaValue.From(
                point.X * point.X +
                point.Y * point.Y +
                values[0] +
                values[1] +
                values[2]);
        });

        runtime.Compile();

        Assert.Equal(67.0, runtime.GetFunction("run").CallDouble());
    }

    [Fact]
    public void Callback_struct_reader_rejects_type_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect(42)
            }
            """);

        runtime.AddModule("host.um", "fn inspect*(value: int): int");
        var callback = runtime.Register("inspect", frame =>
        {
            _ = frame.GetStruct<RealPoint>(0);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<InvalidOperationException>(callback.LastException);
    }

    [Fact]
    public void Callback_struct_reader_rejects_reference_bearing_struct_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspect(host::TextBox{"value"})
            }
            """);

        runtime.AddModule("host.um", """
            type TextBox* = struct {value: str}
            fn inspect*(value: TextBox): int
            """);

        var callback = runtime.Register("inspect", frame =>
        {
            Assert.True(frame.ParameterTypes[0].NativeSize > 0);
            Assert.True(frame.ParameterTypes[0].HasReferences);
            Assert.False(frame.TryGetStruct<NativeStringBox>(0, out var textBox));
            Assert.Equal(default, textBox);
            _ = frame.GetStruct<NativeStringBox>(0);
            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        var ex = Assert.IsType<InvalidOperationException>(callback.LastException);
        Assert.Contains("contains Umka-managed references", ex.Message);
    }

    [Fact]
    public void Callback_result_rejects_type_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::produce()
            }
            """);

        runtime.AddModule("host.um", "fn produce*(): int");
        var callback = runtime.Register("produce", _ => UmkaValue.From("wrong"));

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<ArgumentException>(callback.LastException);
    }

    [Fact]
    public void Callback_frame_exposes_unsupported_result_metadata_and_rejects_writes()
    {
        NativeTestEnvironment.RequireNativeShim();

        AssertUnsupportedCallbackResult(
            "fn makeValue*(): map[str]int",
            UmkaTypeKind.Map,
            expectedMessage: "not host-side map creation, insertion, rooting, ownership transfer, or assignment/reference-count updates");
        AssertUnsupportedCallbackResult(
            """
            type Speaker* = interface {
                speak(): str
            }

            fn makeValue*(): Speaker
            """,
            UmkaTypeKind.Interface,
            assertResultType: resultType => AssertInterfaceMetadata(resultType, expectedItemCount: 3));
        AssertUnsupportedCallbackResult(
            "fn makeValue*(): any",
            UmkaTypeKind.Interface,
            "interface",
            AssertAnyMetadata);
        AssertUnsupportedCallbackResult(
            "fn makeValue*(): fiber",
            UmkaTypeKind.Fiber,
            assertResultType: AssertFiberMetadata);
        AssertUnsupportedCallbackResult(
            """
            type IntFn* = fn (): int
            fn makeValue*(): IntFn
            """,
            UmkaTypeKind.Closure,
            assertResultType: resultType =>
            {
                Assert.True(resultType.HasReferences);
                Assert.True(resultType.IsDeferred);
                Assert.True(resultType.NativeSize >= IntPtr.Size * 2);
            });
    }

    [Fact]
    public void Callback_roundtrips_opaque_weak_pointer_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): bool {
                value := new(int, 42)
                weakValue := weak ^int(value)
                return host::echoWeak(weakValue) == weakValue
            }
            """);

        runtime.AddModule("host.um", "fn echoWeak*(value: weak ^int): weak ^int");
        runtime.Register("echoWeak", frame =>
        {
            var resultType = frame.ResultType;
            Assert.Equal(UmkaTypeKind.WeakPointer, resultType.Kind);
            Assert.False(resultType.IsDeferred);
            Assert.True(resultType.CanReadAsValue());
            Assert.True(resultType.CanReadAsWeakPointer());

            var handle = frame.GetWeakPointer(0);
            var value = UmkaValue.FromWeakPointer(handle);
            Assert.True(frame.CanReturn(value));
            Assert.False(frame.CanReturn(UmkaValue.From(handle)));
            return value;
        });

        runtime.Compile();

        Assert.True(runtime.GetFunction("run").CallBoolean());
    }

    [Fact]
    public void Callback_accepts_void_results_for_void_callbacks()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                host::notify()
                return 42
            }
            """);

        runtime.AddModule("host.um", "fn notify*()");
        var called = false;
        var callback = runtime.Register("notify", _ =>
        {
            called = true;
            return UmkaValue.Void;
        });

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("run").CallInt64());
        Assert.True(called);
        Assert.Null(callback.LastException);
    }

    [Fact]
    public void Callback_result_rejects_out_of_range_narrow_values()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return int(host::produce())
            }
            """);

        runtime.AddModule("host.um", "fn produce*(): int8");
        var callback = runtime.Register("produce", frame =>
        {
            Assert.True(frame.CanReturn(UmkaValue.From(127)));
            Assert.False(frame.CanReturn(UmkaValue.From(128)));

            return UmkaValue.From(128);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<ArgumentOutOfRangeException>(callback.LastException);
    }

    [Fact]
    public void Callback_real32_result_rejects_values_outside_single_range()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): real32 {
                return host::produce()
            }
            """);

        runtime.AddModule("host.um", "fn produce*(): real32");
        var callback = runtime.Register("produce", frame =>
        {
            Assert.True(frame.CanReturn(UmkaValue.From(1.25)));
            Assert.False(frame.CanReturn(UmkaValue.From(double.MaxValue)));

            return UmkaValue.From(double.MaxValue);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallSingle());
        var ex = Assert.IsType<ArgumentOutOfRangeException>(callback.LastException);
        Assert.Contains("finite range", ex.Message);
    }

    [Fact]
    public void Callback_result_rejects_non_void_values_for_void_callbacks()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                host::notify()
                return 42
            }
            """);

        runtime.AddModule("host.um", "fn notify*()");
        var callback = runtime.Register("notify", frame =>
        {
            Assert.True(frame.CanReturn(UmkaValue.Void));
            Assert.False(frame.CanReturn(UmkaValue.From(42)));

            return UmkaValue.From(42);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        Assert.IsType<ArgumentException>(callback.LastException);
    }

    [Fact]
    public void Callback_can_return_fixed_layout_struct_and_static_array_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn pairSum*(): int {
                pair := host::makePair()
                return pair.first + pair.second
            }

            fn arraySum*(): int {
                values := host::makeArray()
                return values[0] + values[1] + values[2]
            }
            """);

        runtime.AddModule("host.um", """
            type Pair* = struct {first, second: int}
            fn makePair*(): Pair
            fn makeArray*(): [3]int
            """);
        runtime.Register("makePair", frame =>
        {
            Assert.Equal(UmkaTypeKind.Struct, frame.ResultType.Kind);
            Assert.Equal(Marshal.SizeOf<IntPair>(), frame.ResultType.NativeSize);
            Assert.False(frame.ResultType.HasReferences);

            var pair = UmkaValue.FromStruct(new IntPair { First = 19, Second = 23 });
            Assert.True(frame.CanReturn(pair));
            Assert.False(frame.CanReturn(UmkaValue.From(42)));
            Assert.False(frame.CanReturn(UmkaValue.FromStaticArray(19L, 23L)));

            return pair;
        });
        runtime.Register("makeArray", frame =>
        {
            Assert.Equal(UmkaTypeKind.StaticArray, frame.ResultType.Kind);
            Assert.Equal(3 * Marshal.SizeOf<long>(), frame.ResultType.NativeSize);
            Assert.Equal(3, frame.ResultType.ItemCount);
            Assert.False(frame.ResultType.HasReferences);

            var values = UmkaValue.FromStaticArray(10L, 14L, 18L);
            Assert.True(frame.CanReturn(values));
            Assert.False(frame.CanReturn(UmkaValue.FromStaticArray(21L, 21L)));
            Assert.False(frame.CanReturn(UmkaValue.FromStaticArray(10, 14, 18)));
            Assert.False(frame.CanReturn(UmkaValue.FromStruct(new IntPair { First = 10, Second = 14 })));

            return values;
        });

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("pairSum").CallInt64());
        Assert.Equal(42, runtime.GetFunction("arraySum").CallInt64());
    }

    [Fact]
    public void Callback_marshals_fixed_layout_weak_pointer_aggregates()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            weakTarget := 42

            fn countArray(values: [2]weak ^int, expected: weak ^int): int {
                total := 0
                for _, value in values {
                    if value == expected {
                        total++
                    }
                }
                return total
            }

            fn run*(): int {
                expected := weak ^int(&weakTarget)
                returnedBox := host::makeBox(expected)
                returnedArray := host::makeArray(expected)
                return host::inspectBox(host::WeakBox{expected}, expected) +
                    host::inspectArray([2]weak ^int{expected, expected}, expected) +
                    (returnedBox.value == expected ? 5 : 0) +
                    countArray(returnedArray, expected)
            }
            """);

        runtime.AddModule("host.um", """
            type WeakBox* = struct {value: weak ^int}
            fn inspectBox*(box: WeakBox, expected: weak ^int): int
            fn inspectArray*(values: [2]weak ^int, expected: weak ^int): int
            fn makeBox*(expected: weak ^int): WeakBox
            fn makeArray*(expected: weak ^int): [2]weak ^int
            """);

        runtime.Register("inspectBox", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.Struct, parameterType.Kind);
            Assert.Equal(Marshal.SizeOf<WeakBox>(), parameterType.NativeSize);
            Assert.False(parameterType.HasReferences);
            Assert.True(frame.CanReadArgumentAsStruct<WeakBox>(0));

            var expected = frame.GetWeakPointer(1);
            var box = frame.GetStruct<WeakBox>(0);
            Assert.Equal(expected, box.Value);
            Assert.True(frame.TryGetStruct<WeakBox>(0, out var tryBox));
            Assert.Equal(expected, tryBox.Value);

            return UmkaValue.From(7);
        });

        runtime.Register("inspectArray", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.StaticArray, parameterType.Kind);
            Assert.Equal(2, parameterType.ItemCount);
            Assert.Equal(2 * Marshal.SizeOf<ulong>(), parameterType.NativeSize);
            Assert.False(parameterType.HasReferences);
            Assert.True(frame.CanReadArgumentAsArray<ulong>(0, 2));

            var expected = frame.GetWeakPointer(1);
            var values = frame.GetArray<ulong>(0, 2);
            Assert.Equal([expected, expected], values);
            Assert.True(frame.TryGetArray<ulong>(0, 2, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal([expected, expected], tryValues);

            return UmkaValue.From(values.Length);
        });

        runtime.Register("makeBox", frame =>
        {
            var expected = frame.GetWeakPointer(0);
            Assert.Equal(UmkaTypeKind.Struct, frame.ResultType.Kind);
            Assert.Equal(Marshal.SizeOf<WeakBox>(), frame.ResultType.NativeSize);
            Assert.False(frame.ResultType.HasReferences);

            var result = UmkaValue.FromStruct(new WeakBox { Value = expected });
            Assert.True(frame.CanReturn(result));
            Assert.False(frame.CanReturn(UmkaValue.From(expected)));

            return result;
        });

        runtime.Register("makeArray", frame =>
        {
            var expected = frame.GetWeakPointer(0);
            Assert.Equal(UmkaTypeKind.StaticArray, frame.ResultType.Kind);
            Assert.Equal(2, frame.ResultType.ItemCount);
            Assert.Equal(2 * Marshal.SizeOf<ulong>(), frame.ResultType.NativeSize);
            Assert.False(frame.ResultType.HasReferences);

            var result = UmkaValue.FromStaticArray(expected, expected);
            Assert.True(frame.CanReturn(result));
            Assert.False(frame.CanReturn(UmkaValue.FromStaticArray(expected)));

            return result;
        });

        runtime.Compile();

        Assert.Equal(16, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_can_return_dynamic_array_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn sum*(): int {
                values := host::makeValues()
                total := 0
                for _, value in values {
                    total += value
                }
                return total
            }

            fn textCount*(): int {
                return len(host::makeTextValues())
            }

            fn textLength*(): int {
                return host::sumText(host::makeTextValues())
            }

            fn inspectAny*(): int {
                return host::inspectAnyValues([]any{42})
            }

            fn anyCount*(): int {
                return len(host::makeAnyValues())
            }
            """);

        runtime.AddModule("host.um", """
            fn makeValues*(): []int
            fn makeTextValues*(): []str
            fn sumText*(values: []str): int
            fn inspectAnyValues*(values: []any): int
            fn makeAnyValues*(): []any
            """);

        runtime.Register("makeValues", frame =>
        {
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.Kind);
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ResultType.ElementKind);
            Assert.Equal("int", frame.ResultType.ElementTypeName);
            Assert.Equal(8, frame.ResultType.ElementNativeSize);
            Assert.False(frame.ResultType.ElementHasReferences);

            var values = UmkaValue.FromDynamicArray(10L, 14L, 18L);
            Assert.True(frame.CanReturn(values));
            Assert.False(frame.CanReturn(UmkaValue.FromStaticArray(10L, 14L, 18L)));
            Assert.False(frame.CanReturn(UmkaValue.FromDynamicArray(10, 14, 18)));

            return values;
        });

        runtime.Register("makeTextValues", frame =>
        {
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.Kind);
            Assert.Equal(UmkaTypeKind.String, frame.ResultType.ElementKind);
            Assert.True(frame.ResultType.ElementHasReferences);
            Assert.True(frame.ResultType.CanReadAsStringArray());

            var value = UmkaValue.FromDynamicArray("a", "bc");
            Assert.True(frame.CanReturn(value));
            Assert.False(frame.CanReturn(UmkaValue.FromDynamicArray(new[] { IntPtr.Zero })));
            return value;
        });

        runtime.Register("sumText", frame =>
        {
            Assert.True(frame.CanReadArgumentAsStringArray(0));
            Assert.False(frame.CanReadArgumentAsDynamicArray<IntPtr>(0));

            var values = frame.GetStringArray(0);
            Assert.True(frame.TryGetStringArray(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal(values, tryValues);

            return UmkaValue.From(values.Sum(value => value?.Length ?? 0));
        });

        runtime.Register("inspectAnyValues", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.Interface, parameterType.ElementKind);
            Assert.True(parameterType.ElementHasReferences);
            Assert.True(parameterType.IsDeferred);
            Assert.False(frame.CanReadArgumentAsDynamicArray<IntPtr>(0));
            Assert.False(frame.TryGetDynamicArray<IntPtr>(0, out var values));
            Assert.Null(values);
            var ex = Assert.Throws<InvalidOperationException>(() => frame.GetDynamicArray<IntPtr>(0));
            Assert.Contains("element type", ex.Message);
            Assert.Contains("contains Umka-managed references", ex.Message);

            return UmkaValue.From(1);
        });

        var makeAnyValues = runtime.Register("makeAnyValues", frame =>
        {
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.Kind);
            Assert.Equal(UmkaTypeKind.Interface, frame.ResultType.ElementKind);
            Assert.True(frame.ResultType.ElementHasReferences);
            Assert.True(frame.ResultType.IsDeferred);

            var value = UmkaValue.FromDynamicArray(42L);
            Assert.False(frame.CanReturn(value));
            return value;
        });

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("sum").CallInt64());
        Assert.Equal(2, runtime.GetFunction("textCount").CallInt64());
        Assert.Equal(3, runtime.GetFunction("textLength").CallInt64());
        Assert.Equal(1, runtime.GetFunction("inspectAny").CallInt64());

        Assert.Throws<UmkaException>(() => runtime.GetFunction("anyCount").CallInt64());
        var callbackEx = Assert.IsType<ArgumentException>(makeAnyValues.LastException);
        Assert.Contains("element type", callbackEx.Message);
        Assert.Contains("contains Umka-managed references", callbackEx.Message);
    }

    [Fact]
    public void Callback_can_return_nested_dynamic_array_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn matrixSum*(): int {
                values := host::makeMatrix()
                total := 0
                for _, row in values {
                    for _, value in row {
                        total += value
                    }
                }
                return total
            }

            fn textMatrixLength*(): int {
                values := host::makeTextMatrix()
                total := 0
                for _, row in values {
                    for _, value in row {
                        total += len(value)
                    }
                }
                return total
            }
            """);

        runtime.AddModule("host.um", """
            fn makeMatrix*(): [][]int
            fn makeTextMatrix*(): [][]str
            """);

        var matrixRows = new[] { new[] { 10L, 14L }, Array.Empty<long>(), new[] { 18L } };
        var wrongMatrixRows = new[] { new[] { 1, 2 } };
        var stringMatrixRows = new[] { new string?[] { "x" } };
        var textMatrixRows = new[] { new string?[] { "um", "ka" }, Array.Empty<string?>(), new string?[] { "sharp" } };
        var wrongTextMatrixRows = new[] { new[] { 1L } };

        runtime.Register("makeMatrix", frame =>
        {
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.ElementKind);
            Assert.Equal(UmkaTypeKind.SignedInteger, frame.ResultType.NestedElementKind);
            Assert.Equal(8, frame.ResultType.NestedElementNativeSize);
            Assert.False(frame.ResultType.NestedElementHasReferences);

            var value = UmkaValue.FromNestedDynamicArray(matrixRows);
            Assert.True(frame.CanReturn(value));
            Assert.False(frame.CanReturn(UmkaValue.FromDynamicArray(10L, 14L, 18L)));
            Assert.False(frame.CanReturn(UmkaValue.FromNestedDynamicArray(wrongMatrixRows)));
            Assert.False(frame.CanReturn(UmkaValue.FromNestedDynamicArray(stringMatrixRows)));
            return value;
        });

        runtime.Register("makeTextMatrix", frame =>
        {
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, frame.ResultType.ElementKind);
            Assert.Equal(UmkaTypeKind.String, frame.ResultType.NestedElementKind);
            Assert.Equal(IntPtr.Size, frame.ResultType.NestedElementNativeSize);
            Assert.True(frame.ResultType.NestedElementHasReferences);
            Assert.True(frame.ResultType.CanReadAsNestedStringArray());

            var value = UmkaValue.FromNestedDynamicArray(textMatrixRows);
            Assert.True(frame.CanReturn(value));
            Assert.False(frame.CanReturn(UmkaValue.FromDynamicArray("um", "ka")));
            Assert.False(frame.CanReturn(UmkaValue.FromNestedDynamicArray(wrongTextMatrixRows)));
            return value;
        });

        runtime.Compile();

        Assert.Equal(42, runtime.GetFunction("matrixSum").CallInt64());
        Assert.Equal(9, runtime.GetFunction("textMatrixLength").CallInt64());
    }

    [Fact]
    public void Callback_marshals_dynamic_arrays_of_opaque_weak_pointer_handles()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            weakTarget := 42

            fn countWeakValues(values: []weak ^int, expected: weak ^int): int {
                total := 0
                for _, value in values {
                    if value == expected {
                        total++
                    }
                }
                return total
            }

            fn run*(): int {
                expected := weak ^int(&weakTarget)
                values := []weak ^int{expected, expected}
                returned := host::makeWeakValues(expected)
                return host::countWeakValues(values, expected) + countWeakValues(returned, expected)
            }
            """);

        runtime.AddModule("host.um", """
            fn countWeakValues*(values: []weak ^int, expected: weak ^int): int
            fn makeWeakValues*(expected: weak ^int): []weak ^int
            """);

        runtime.Register("countWeakValues", frame =>
        {
            var parameterType = frame.ParameterTypes[0];
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.WeakPointer, parameterType.ElementKind);
            Assert.Equal(8, parameterType.ElementNativeSize);
            Assert.False(parameterType.ElementHasReferences);
            Assert.True(frame.CanReadArgumentAsDynamicArray<ulong>(0));
            Assert.False(frame.CanReadArgumentAsDynamicArray<uint>(0));

            var expected = frame.GetWeakPointer(1);
            var values = frame.GetDynamicArray<ulong>(0);
            Assert.Equal([expected, expected], values);
            Assert.True(frame.TryGetDynamicArray<ulong>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal([expected, expected], tryValues);
            Assert.False(frame.TryGetDynamicArray<uint>(0, out var wrongElementSize));
            Assert.Null(wrongElementSize);

            return UmkaValue.From(values.Count(value => value == expected));
        });

        runtime.Register("makeWeakValues", frame =>
        {
            var expected = frame.GetWeakPointer(0);
            var resultType = frame.ResultType;
            Assert.Equal(UmkaTypeKind.DynamicArray, resultType.Kind);
            Assert.Equal(UmkaTypeKind.WeakPointer, resultType.ElementKind);
            Assert.Equal(8, resultType.ElementNativeSize);
            Assert.False(resultType.ElementHasReferences);

            var result = UmkaValue.FromDynamicArray(expected, expected);
            Assert.True(frame.CanReturn(result));
            Assert.False(frame.CanReturn(UmkaValue.FromDynamicArray(1U, 2U)));

            return result;
        });

        runtime.Compile();

        Assert.Equal(4, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_frame_copies_nested_dynamic_array_arguments()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return host::inspectMatrix([][]int{[]int{1, 2}, []int{}, []int{3, 4, 5}}) +
                    host::inspectTextMatrix([][]str{[]str{"a", "b"}, []str{}, []str{"c"}}) +
                    host::inspectAnyMatrix([][]any{[]any{42}})
            }
            """);

        runtime.AddModule("host.um", """
            fn inspectMatrix*(values: [][]int): int
            fn inspectTextMatrix*(values: [][]str): int
            fn inspectAnyMatrix*(values: [][]any): int
            """);

        runtime.Register("inspectMatrix", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.ElementKind);
            Assert.Equal("[]int", parameterType.ElementTypeName);
            Assert.True(parameterType.ElementHasReferences);
            Assert.Equal(UmkaTypeKind.SignedInteger, parameterType.NestedElementKind);
            Assert.Equal("int", parameterType.NestedElementTypeName);
            Assert.Equal(8, parameterType.NestedElementNativeSize);
            Assert.False(parameterType.NestedElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(frame.CanReadArgumentAsNestedDynamicArray<long>(0));
            Assert.False(frame.CanReadArgumentAsNestedDynamicArray<int>(0));
            Assert.False(frame.CanReadArgumentAsDynamicArray<IntPtr>(0));

            var values = frame.GetNestedDynamicArray<long>(0);
            Assert.Equal(3, values.Length);
            Assert.Equal([1L, 2L], values[0]);
            Assert.Empty(values[1]);
            Assert.Equal([3L, 4L, 5L], values[2]);
            Assert.True(frame.TryGetNestedDynamicArray<long>(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Equal([1L, 2L], tryValues[0]);
            Assert.Empty(tryValues[1]);
            Assert.Equal([3L, 4L, 5L], tryValues[2]);
            Assert.False(frame.TryGetNestedDynamicArray<int>(0, out var wrongElementSize));
            Assert.Null(wrongElementSize);

            return UmkaValue.From(values.Sum(row => row.Sum()));
        });

        runtime.Register("inspectTextMatrix", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.ElementKind);
            Assert.Equal(UmkaTypeKind.String, parameterType.NestedElementKind);
            Assert.Equal("str", parameterType.NestedElementTypeName);
            Assert.Equal(IntPtr.Size, parameterType.NestedElementNativeSize);
            Assert.True(parameterType.NestedElementHasReferences);
            Assert.False(parameterType.IsDeferred);
            Assert.True(parameterType.CanReadAsNestedStringArray());
            Assert.True(frame.CanReadArgumentAsNestedStringArray(0));
            Assert.False(frame.CanReadArgumentAsNestedDynamicArray<IntPtr>(0));

            var values = frame.GetNestedStringArray(0);
            Assert.Equal(3, values.Length);
            Assert.Collection(
                values[0],
                item => Assert.Equal("a", item),
                item => Assert.Equal("b", item));
            Assert.Empty(values[1]);
            Assert.Collection(values[2], item => Assert.Equal("c", item));
            Assert.True(frame.TryGetNestedStringArray(0, out var tryValues));
            Assert.NotNull(tryValues);
            Assert.Collection(
                tryValues[0],
                item => Assert.Equal("a", item),
                item => Assert.Equal("b", item));
            Assert.Empty(tryValues[1]);
            Assert.Collection(tryValues[2], item => Assert.Equal("c", item));

            return UmkaValue.From(values.SelectMany(row => row).Sum(value => value?.Length ?? 0));
        });

        runtime.Register("inspectAnyMatrix", frame =>
        {
            var parameterType = Assert.Single(frame.ParameterTypes);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.Kind);
            Assert.Equal(UmkaTypeKind.DynamicArray, parameterType.ElementKind);
            Assert.Equal(UmkaTypeKind.Interface, parameterType.NestedElementKind);
            Assert.True(parameterType.NestedElementHasReferences);
            Assert.True(parameterType.IsDeferred);
            Assert.False(parameterType.CanReadAsNestedStringArray());
            Assert.False(frame.CanReadArgumentAsNestedStringArray(0));
            Assert.False(frame.TryGetNestedStringArray(0, out var values));
            Assert.Null(values);
            var ex = Assert.Throws<InvalidOperationException>(() => frame.GetNestedStringArray(0));
            Assert.Contains("jagged string array", ex.Message);

            return UmkaValue.From(1);
        });

        runtime.Compile();

        Assert.Equal(19, runtime.GetFunction("run").CallInt64());
    }

    [Fact]
    public void Callback_can_read_and_return_static_arrays_of_fixed_layout_structs()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn inspectPoints*(): real {
                return host::inspect([2]host::Point{host::Point{1.5, 2.5}, host::Point{3.5, 4.5}})
            }

            fn makePointsSum*(): real {
                points := host::makePoints()
                return points[0].x + points[0].y + points[1].x + points[1].y
            }
            """);

        runtime.AddModule("host.um", """
            type Point* = struct {x, y: real}
            fn inspect*(points: [2]Point): real
            fn makePoints*(): [2]Point
            """);
        runtime.Register("inspect", frame =>
        {
            Assert.Equal(UmkaTypeKind.StaticArray, frame.ParameterTypes[0].Kind);
            Assert.Equal(2 * Marshal.SizeOf<RealPoint>(), frame.ParameterTypes[0].NativeSize);
            Assert.Equal(2, frame.ParameterTypes[0].ItemCount);
            Assert.False(frame.ParameterTypes[0].HasReferences);

            var points = frame.GetArray<RealPoint>(0, 2);
            Assert.True(frame.TryGetArray<RealPoint>(0, 2, out var tryPoints));
            Assert.NotNull(tryPoints);
            Assert.Equal(points.Select(point => point.X + point.Y), tryPoints.Select(point => point.X + point.Y));

            return UmkaValue.From(points.Sum(point => point.X + point.Y));
        });
        runtime.Register("makePoints", frame =>
        {
            Assert.Equal(UmkaTypeKind.StaticArray, frame.ResultType.Kind);
            Assert.Equal(2 * Marshal.SizeOf<RealPoint>(), frame.ResultType.NativeSize);
            Assert.Equal(2, frame.ResultType.ItemCount);
            Assert.False(frame.ResultType.HasReferences);

            return UmkaValue.FromStaticArray(
                new RealPoint { X = 1.5, Y = 2.5 },
                new RealPoint { X = 3.5, Y = 4.5 });
        });

        runtime.Compile();

        Assert.Equal(12.0, runtime.GetFunction("inspectPoints").CallDouble());
        Assert.Equal(12.0, runtime.GetFunction("makePointsSum").CallDouble());
    }

    [Fact]
    public void Callback_can_return_narrow_scalar_results()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn getInt8*(): int8 {
                return host::getInt8()
            }

            fn getInt16*(): int16 {
                return host::getInt16()
            }

            fn getInt32*(): int32 {
                return host::getInt32()
            }

            fn getUInt8*(): uint8 {
                return host::getUInt8()
            }

            fn getUInt16*(): uint16 {
                return host::getUInt16()
            }

            fn getUInt32*(): uint32 {
                return host::getUInt32()
            }

            fn getChar*(): char {
                return host::getChar()
            }

            fn getReal32*(): real32 {
                return host::getReal32()
            }
            """);

        runtime.AddModule("host.um", """
            fn getInt8*(): int8
            fn getInt16*(): int16
            fn getInt32*(): int32
            fn getUInt8*(): uint8
            fn getUInt16*(): uint16
            fn getUInt32*(): uint32
            fn getChar*(): char
            fn getReal32*(): real32
            """);

        runtime.Register("getInt8", _ => UmkaValue.From((sbyte)-8));
        runtime.Register("getInt16", _ => UmkaValue.From((short)-1600));
        runtime.Register("getInt32", _ => UmkaValue.From(-32000));
        runtime.Register("getUInt8", _ => UmkaValue.From((byte)200));
        runtime.Register("getUInt16", _ => UmkaValue.From((ushort)65000));
        runtime.Register("getUInt32", _ => UmkaValue.From(4_000_000_000U));
        runtime.Register("getChar", _ => UmkaValue.From('A'));
        runtime.Register("getReal32", _ => UmkaValue.From(1.25f));

        runtime.Compile();

        Assert.Equal((sbyte)-8, runtime.GetFunction("getInt8").CallSByte());
        Assert.Equal((short)-1600, runtime.GetFunction("getInt16").CallInt16());
        Assert.Equal(-32000, runtime.GetFunction("getInt32").CallInt32());
        Assert.Equal((byte)200, runtime.GetFunction("getUInt8").CallByte());
        Assert.Equal((ushort)65000, runtime.GetFunction("getUInt16").CallUInt16());
        Assert.Equal(4_000_000_000U, runtime.GetFunction("getUInt32").CallUInt32());
        Assert.Equal('A', runtime.GetFunction("getChar").CallChar());
        Assert.Equal(1.25f, runtime.GetFunction("getReal32").CallSingle());
    }

    [Fact]
    public void Callback_structured_result_rejects_type_mismatches()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): [2]int {
                return host::makePair()
            }
            """);

        runtime.AddModule("host.um", "fn makePair*(): [2]int");
        var callback = runtime.Register("makePair", _ => UmkaValue.From(42));

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallStruct<IntPair>());
        var ex = Assert.IsType<ArgumentException>(callback.LastException);
        Assert.Contains("value kind Int", ex.Message);
    }

    [Fact]
    public void Callback_structured_result_rejects_reference_bearing_result_types()
    {
        NativeTestEnvironment.RequireNativeShim();

        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                return len(host::makeText().value)
            }
            """);

        runtime.AddModule("host.um", """
            type TextBox* = struct {value: str}
            fn makeText*(): TextBox
            """);

        var callback = runtime.Register("makeText", frame =>
        {
            Assert.True(frame.ResultType.NativeSize > 0);
            Assert.True(frame.ResultType.HasReferences);
            Assert.False(frame.CanReturn(UmkaValue.FromStruct(new NativeStringBox())));

            return UmkaValue.FromStruct(new NativeStringBox());
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        var ex = Assert.IsType<ArgumentException>(callback.LastException);
        Assert.Contains("contains Umka-managed references", ex.Message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IntPair
    {
        public long First;
        public long Second;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RealPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStringBox
    {
        public IntPtr Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WeakBox
    {
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ManagedStringBox
    {
        public string? Value;
    }

    private enum HostColor
    {
        Red,
        Green,
        Blue
    }

    private enum HostMode : byte
    {
        Draw = 74,
        Select,
        Remove = 8,
        Edit
    }

    private static UmkaValue AssertUnsupportedCallbackArgument(
        UmkaCallFrame frame,
        UmkaTypeKind expectedKind,
        string? expectedTypeName = null)
    {
        Assert.Equal(1, frame.ParameterCount);
        Assert.Equal(expectedKind, frame.ParameterTypes[0].Kind);
        if (expectedTypeName is not null)
            Assert.Contains(expectedTypeName, frame.ParameterTypes[0].TypeName);

        Assert.Throws<InvalidOperationException>(() => frame.GetInt64(0));
        Assert.Throws<InvalidOperationException>(() => frame.GetString(0));
        Assert.Throws<InvalidOperationException>(() => frame.GetPointer(0));
        Assert.Throws<InvalidOperationException>(() => frame.GetStruct<IntPair>(0));
        Assert.Throws<InvalidOperationException>(() => frame.GetValue(0));
        Assert.False(frame.CanReadArgumentAsValue(0));
        Assert.False(frame.CanReadArgumentAsScalar<int>(0));
        Assert.False(frame.CanReadArgumentAsStruct<IntPair>(0));
        Assert.False(frame.CanReadArgumentAsArray<long>(0, 1));
        Assert.False(frame.TryGetValue(0, out var dynamicValue));
        Assert.Equal(UmkaValueKind.Void, dynamicValue.Kind);

        return UmkaValue.From(1);
    }

    private static void AssertUnsupportedCallbackResult(
        string hostModuleSource,
        UmkaTypeKind expectedKind,
        string? expectedTypeName = null,
        Action<UmkaTypeInfo>? assertResultType = null,
        string? expectedMessage = null)
    {
        using var runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn run*(): int {
                value := host::makeValue()
                return 42
            }
            """);

        runtime.AddModule("host.um", hostModuleSource);
        var callback = runtime.Register("makeValue", frame =>
        {
            Assert.Equal(0, frame.ParameterCount);
            Assert.Equal(expectedKind, frame.ResultType.Kind);
            if (expectedTypeName is not null)
                Assert.Contains(expectedTypeName, frame.ResultType.TypeName);
            assertResultType?.Invoke(frame.ResultType);

            Assert.False(frame.CanReturn(UmkaValue.From(0)));

            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        var ex = Assert.IsType<ArgumentException>(callback.LastException);
        Assert.Contains(expectedMessage ?? "does not support as a callback result", ex.Message);
    }

    private static void AssertInterfaceMetadata(UmkaTypeInfo type, int expectedItemCount)
    {
        Assert.Equal(UmkaTypeKind.Interface, type.Kind);
        Assert.True(type.HasReferences);
        Assert.True(type.IsDeferred);
        Assert.Equal(expectedItemCount, type.ItemCount);
        Assert.True(type.NativeSize >= IntPtr.Size * 2);
        Assert.False(type.CanReadAsValue());
    }

    private static void AssertAnyMetadata(UmkaTypeInfo type)
    {
        AssertInterfaceMetadata(type, expectedItemCount: 2);
        Assert.Contains("interface", type.TypeName);
        Assert.Equal(IntPtr.Size * 2, type.NativeSize);
    }

    private static void AssertFiberMetadata(UmkaTypeInfo type)
    {
        Assert.Equal(UmkaTypeKind.Fiber, type.Kind);
        Assert.Equal("fiber", type.TypeName);
        Assert.True(type.HasReferences);
        Assert.True(type.IsDeferred);
        Assert.Equal(IntPtr.Size, type.NativeSize);
        Assert.False(type.CanReadAsValue());
    }
}
