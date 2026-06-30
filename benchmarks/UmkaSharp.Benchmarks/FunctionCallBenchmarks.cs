using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;

namespace UmkaSharp.Benchmarks;

/// <summary>Measures steady-state C# to Umka calls and Umka to C# callback round trips.</summary>
[MemoryDiagnoser]
public class FunctionCallBenchmarks
{
    private const string DirectNativeSource = """
        fn add*(a, b: int): int {
            return a + b
        }
        """;

    private readonly UmkaValue[] _intArgs = [UmkaValue.From(19), UmkaValue.From(23)];
    private readonly UmkaValue[] _stringArgs = [UmkaValue.From("Umka")];
    private readonly UmkaValue[] _structArgs = [UmkaValue.From(2.5), UmkaValue.From(7.5)];
    private readonly UmkaValue[] _callbackArgs = [UmkaValue.From(41)];
    private UmkaValue[] _hostHandleArgs = null!;
    private readonly Func<long, long, long> _managedAdd = static (a, b) => a + b;
    private readonly Func<long, long> _managedInc = static value => value + 1;
    private readonly HostRules _hostRules = new(7);
    private long _left = 19;
    private long _right = 23;
    private long _callbackValue = 41;
    private string _name = "Umka";
    private double _x = 2.5;
    private double _y = 7.5;

    private UmkaRuntime _runtime = null!;
    private IntPtr _nativeRuntime;
    private UmkaFunction _add = null!;
    private UmkaFunction _greet = null!;
    private UmkaFunction _pair = null!;
    private UmkaFunction _callback = null!;
    private UmkaFunction _scalarCallback = null!;
    private UmkaFunction _hostHandleCallback = null!;
    private UmkaHostHandle _hostRulesHandle = null!;
    private NativeFunctionContext _nativeAdd;

    [GlobalSetup]
    public void Setup()
    {
        _runtime = UmkaRuntime.FromSource("""
            import "host.um"

            fn add*(a, b: int): int {
                return a + b
            }

            fn greet*(name: str): str {
                return "Hello, " + name
            }

            fn pair*(x, y: real): [2]real {
                return [2]real{x, y}
            }

            fn callbackAdd*(value: int): int {
                return host::inc(value)
            }

            fn scalarCallbackAdd*(value: int): int {
                return host::scalarInc(value)
            }

            fn hostHandleBonus*(rules: ^void): int {
                return host::bonus(rules)
            }
            """);

        _runtime.AddModule("host.um", """
            fn inc*(value: int): int
            fn scalarInc*(value: int): int
            fn bonus*(rules: ^void): int
            """);
        _runtime.Register("inc", frame => UmkaValue.From(frame.GetInt64(0) + 1));
        _runtime.Register("scalarInc", frame => UmkaValue.FromScalar(frame.GetScalar<long>(0) + 1));
        _runtime.Register("bonus", frame => UmkaValue.From(frame.GetHostObject<HostRules>(0).Bonus));
        _hostRulesHandle = _runtime.CreateHostHandle(_hostRules);
        _hostHandleArgs = [UmkaValue.FromHostHandle(_hostRulesHandle)];
        _runtime.Compile();

        _add = _runtime.GetFunction("add");
        _greet = _runtime.GetFunction("greet");
        _pair = _runtime.GetFunction("pair");
        _callback = _runtime.GetFunction("callbackAdd");
        _scalarCallback = _runtime.GetFunction("scalarCallbackAdd");
        _hostHandleCallback = _runtime.GetFunction("hostHandleBonus");

        _nativeRuntime = CreateNativeRuntime();
        ThrowIfNativeError(NativeMethods.GetFunction(_nativeRuntime, null, "add", out _nativeAdd));
        if (CallDirectNativeShimAdd() != 42)
            throw new InvalidOperationException("Native shim benchmark returned an unexpected result.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_nativeRuntime != IntPtr.Zero)
        {
            NativeMethods.Free(_nativeRuntime);
            _nativeRuntime = IntPtr.Zero;
        }

        _runtime.Dispose();
    }

    [Benchmark]
    public long DirectManagedIntAdd() => _left + _right;

    [Benchmark]
    public long IntFunctionCall() => _add.CallInt64(_intArgs);

    [Benchmark]
    public long ScalarIntFunctionCall() => _add.CallScalar<long>(_intArgs);

    [Benchmark]
    public long IntFunctionCallInlineParams() => _add.CallInt64(UmkaValue.From(_left), UmkaValue.From(_right));

    [Benchmark]
    public long DirectManagedDynamicIntValue() => UmkaValue.From(_managedAdd(_left, _right)).AsInt64();

    [Benchmark]
    public long DynamicIntFunctionCall() => _add.CallValue(_intArgs).AsInt64();

    [Benchmark]
    public long DynamicIntFunctionCallInlineParams() =>
        _add.CallValue(UmkaValue.From(_left), UmkaValue.From(_right)).AsInt64();

    [Benchmark]
    public long DirectNativeShimIntFunctionCall()
        => CallDirectNativeShimAdd();

    private long CallDirectNativeShimAdd()
    {
        ThrowIfNativeError(NativeMethods.ContextSetArgInt(_nativeRuntime, ref _nativeAdd, 0, _left));
        ThrowIfNativeError(NativeMethods.ContextSetArgInt(_nativeRuntime, ref _nativeAdd, 1, _right));
        ThrowIfNativeError(NativeMethods.Call(_nativeRuntime, ref _nativeAdd));
        return NativeMethods.ContextGetResultInt(ref _nativeAdd);
    }

    [Benchmark]
    public string DirectManagedStringConcat() => "Hello, " + _name;

    [Benchmark]
    public string? StringFunctionCall() => _greet.CallString(_stringArgs);

    [Benchmark]
    public RealPair DirectManagedStructCreate() => new() { X = _x, Y = _y };

    [Benchmark]
    public RealPair StructResultCall() => _pair.CallStruct<RealPair>(_structArgs);

    [Benchmark]
    public long DirectManagedDelegateCall() => _managedInc(_managedAdd(_callbackValue - 1, 1));

    [Benchmark]
    public long CallbackRoundTrip() => _callback.CallInt64(_callbackArgs);

    [Benchmark]
    public long ScalarCallbackRoundTrip() => _scalarCallback.CallScalar<long>(_callbackArgs);

    [Benchmark]
    public long DirectManagedHostObjectRead() => _hostRules.Bonus;

    [Benchmark]
    public long HostHandleCallbackRoundTrip() => _hostHandleCallback.CallInt64(_hostHandleArgs);

    [StructLayout(LayoutKind.Sequential)]
    public struct RealPair
    {
        public double X;
        public double Y;
    }

    private sealed class HostRules(long bonus)
    {
        public long Bonus { get; } = bonus;
    }

    private static IntPtr CreateNativeRuntime()
    {
        ThrowIfNativeError(NativeMethods.Create(
            "benchmark.um",
            DirectNativeSource,
            stackSize: 1024 * 1024,
            argumentCount: 0,
            arguments: IntPtr.Zero,
            fileSystemEnabled: 0,
            implLibsEnabled: 0,
            warningCallback: IntPtr.Zero,
            out var runtime));

        try
        {
            ThrowIfNativeError(NativeMethods.Compile(runtime));
        }
        catch
        {
            NativeMethods.Free(runtime);
            throw;
        }

        return runtime;
    }

    private static void ThrowIfNativeError(int status)
    {
        if (status != 0)
            throw new InvalidOperationException($"Native shim benchmark call failed with status {status}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFunctionContext
    {
        public long EntryOffset;
        public IntPtr Parameters;
        public IntPtr Result;
    }

#pragma warning disable CA2101 // String parameters use explicit LPUTF8Str marshalling for Umka's UTF-8 C ABI.
    private static partial class NativeMethods
    {
        private const string LibraryName = "umka_shim";

        [DllImport(LibraryName, EntryPoint = "ushim_create", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int Create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string fileName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string source,
            int stackSize,
            int argumentCount,
            IntPtr arguments,
            int fileSystemEnabled,
            int implLibsEnabled,
            IntPtr warningCallback,
            out IntPtr runtime);

        [DllImport(LibraryName, EntryPoint = "ushim_free", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Free(IntPtr runtime);

        [DllImport(LibraryName, EntryPoint = "ushim_compile", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Compile(IntPtr runtime);

        [DllImport(LibraryName, EntryPoint = "ushim_get_function", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int GetFunction(
            IntPtr runtime,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? moduleName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string functionName,
            out NativeFunctionContext function);

        [DllImport(LibraryName, EntryPoint = "ushim_context_set_arg_int", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ContextSetArgInt(IntPtr runtime, ref NativeFunctionContext function, int index, long value);

        [DllImport(LibraryName, EntryPoint = "ushim_call", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Call(IntPtr runtime, ref NativeFunctionContext function);

        [DllImport(LibraryName, EntryPoint = "ushim_context_get_result_int", CallingConvention = CallingConvention.Cdecl)]
        internal static extern long ContextGetResultInt(ref NativeFunctionContext function);
    }
#pragma warning restore CA2101
}
