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
                    host::inspectMap(map[str]int{"answer": 42}) +
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
            fn inspectMap*(value: map[str]int): int
            fn inspectInterface*(value: Speaker): int
            fn inspectAny*(value: any): int
            fn inspectClosure*(value: IntFn): int
            fn inspectWeak*(value: weak ^int): int
            fn inspectFiber*(value: fiber): int
            """);

        runtime.Register("inspectDynamicArray", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.DynamicArray));
        runtime.Register("inspectMap", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Map));
        runtime.Register("inspectInterface", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Interface));
        runtime.Register("inspectAny", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Interface, "interface"));
        runtime.Register("inspectClosure", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Closure));
        runtime.Register("inspectWeak", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.WeakPointer));
        runtime.Register("inspectFiber", frame => AssertUnsupportedCallbackArgument(frame, UmkaTypeKind.Fiber));

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

        AssertUnsupportedCallbackResult("fn makeValue*(): []int", UmkaTypeKind.DynamicArray);
        AssertUnsupportedCallbackResult("fn makeValue*(): map[str]int", UmkaTypeKind.Map);
        AssertUnsupportedCallbackResult(
            """
            type Speaker* = interface {
                speak(): str
            }

            fn makeValue*(): Speaker
            """,
            UmkaTypeKind.Interface);
        AssertUnsupportedCallbackResult("fn makeValue*(): any", UmkaTypeKind.Interface, "interface");
        AssertUnsupportedCallbackResult("fn makeValue*(): weak ^int", UmkaTypeKind.WeakPointer);
        AssertUnsupportedCallbackResult("fn makeValue*(): fiber", UmkaTypeKind.Fiber);
        AssertUnsupportedCallbackResult(
            """
            type IntFn* = fn (): int
            fn makeValue*(): IntFn
            """,
            UmkaTypeKind.Closure);
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
        string? expectedTypeName = null)
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

            Assert.False(frame.CanReturn(UmkaValue.From(0)));

            return UmkaValue.From(0);
        });

        runtime.Compile();

        Assert.Throws<UmkaException>(() => runtime.GetFunction("run").CallInt64());
        var ex = Assert.IsType<ArgumentException>(callback.LastException);
        Assert.Contains("does not support as a callback result", ex.Message);
    }
}
