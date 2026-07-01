using System.Runtime.InteropServices;
using UmkaSharp;

var warnings = new List<UmkaError>();
const string FileWriterSource = """
    import "std.um"

    fn writeText*(name: str): int {
        f, err := std::fopen(name, "wb")
        if err.code != 0 {
            return err.code
        }

        data := []char("Hello from package")
        _, writeErr := std::fwrite(f, &data)
        if writeErr.code != 0 {
            std::fclose(f)
            return writeErr.code
        }

        closeErr := std::fclose(f)
        return closeErr.code
    }
    """;
using var runtime = UmkaRuntime.CompileSource(
    """
    import (
        "host.um"
        "math.um"
        "std.um"
    )

    type Pair = struct {
        x, y: int
    }

    type Mode = enum (uint8) {
        draw = 74
        select
        remove = 8
        edit
    }

    type IntFn = fn (): int

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

    weakTarget := 42

    fn score*(rules: ^void, pair: Pair): int {
        return host::bonus(rules, pair.x + pair.y)
    }

    fn echoHandle*(rules: ^void): ^void {
        return rules
    }

    fn tryHandle*(rules, maybe: ^void): int {
        return host::tryHandle(rules, maybe)
    }

    fn enumRoundtrip*(mode: Mode): Mode

    fn enumProbe*(): Mode {
        return enumRoundtrip(.remove)
    }

    fn scalarRoundtrip*(i: int, u: uint, r: real, b: bool, c: char, s: str, p: ^void, mode: Mode): int

    fn scalarCallback*(): int {
        return scalarRoundtrip(-42, uint(42), 12.25, true, 'A', "value", null, .select)
    }

    fn scalarInputs*(i: int, u: uint, r: real, b: bool, c: char, s: str, p: ^void, mode: Mode): int {
        if !b || c != 'A' || s != "value" || p != null || mode != .select {
            return -1
        }
        return i + int(u)
    }

    fn scalarMode*(): Mode {
        return .select
    }

    fn range*(center, radius: real): [2]real {
        return [2]real{center - radius, center + radius}
    }

    fn values*(): [3]int {
        return [3]int{10, 14, 18}
    }

    fn takeDynamicArray*(values: []int): int {
        return len(values)
    }

    fn takeMap*(value: map[str]int): int {
        return 0
    }

    fn takeInterface*(value: Speaker): int {
        return 0
    }

    fn takeAny*(value: any): int {
        return 0
    }

    fn takeClosure*(value: IntFn): int {
        return 0
    }

    fn takeWeakPointer*(value: weak ^int): int {
        return 0
    }

    fn takeFiber*(value: fiber): int {
        return 0
    }

    fn dynamicArrayValue*(): []int {
        return []int{1, 2, 3}
    }

    fn mapValue*(): map[str]int {
        return map[str]int{"answer": 42}
    }

    fn interfaceValue*(): Speaker {
        return Dog{woofCount: 0}
    }

    fn closureValue*(): IntFn {
        return fn (): int {
            return 42
        }
    }

    fn fiberValue*(): fiber {
        return make(fiber, fn() {})
    }

    fn weakPointerValue*(): weak ^int {
        return weak ^int(&weakTarget)
    }

    fn anyValue*(): any {
        return 42
    }

    fn narrow*(): int {
        return host::narrow(int8(-8), uint16(65000), 'A', real32(1.25))
    }

    fn letter*(): char {
        return 'A'
    }

    fn single*(): real32 {
        return real32(1.25)
    }

    fn warningProbe*(): int {
        unused := 1
        return 42
    }

    fn argumentSummary*(): str {
        if std::argc() != 3 {
            return "bad-count"
        }
        return std::argv(0) + ":" + std::argv(1) + ":" + std::argv(2)
    }

    fn truth*(): bool {
        return true
    }

    fn ratio*(): real {
        return 2.5
    }

    fn nullHandle*(): ^void {
        return null
    }

    fn tick*() {
    }

    fn hiddenPackageValue(): int {
        return 42
    }

    fn exportProbe*(): int {
        return hiddenPackageValue()
    }
    """,
    new UmkaRuntimeOptions
    {
        Arguments = ["package-consumer.um", "alpha", "beta"],
        WarningHandler = warnings.Add,
    },
    configure: configured =>
    {
        configured.AddModule("host.um", """
            fn bonus*(rules: ^void, value: int): int
            fn tryHandle*(rules, maybe: ^void): int
            fn narrow*(i8: int8, u16: uint16, c: char, r32: real32): int
            """);
        configured.AddModule("math.um", """
            fn hiddenAddFee(value, fee: int): int {
                return value + fee
            }

            fn addFee*(value, fee: int): int {
                return hiddenAddFee(value, fee)
            }
            """);
        configured.Register("bonus", frame =>
        {
            var bonus = frame.GetHostObject<long>(0);
            return UmkaValue.From(frame.GetInt64(1) + bonus);
        });
        configured.Register("tryHandle", frame =>
        {
            if (!frame.TryGetHostObject<long>(0, out var bonus))
                throw new InvalidOperationException("Expected callback host handle resolution to succeed.");

            if (frame.TryGetHostObject<long>(1, out _))
                throw new InvalidOperationException("Unexpected callback host handle resolution for null pointer.");

            return UmkaValue.From(bonus);
        });
        configured.Register("narrow", frame =>
        {
            if (frame.GetSByte(0) != -8 ||
                frame.GetUInt16(1) != 65000 ||
                frame.GetChar(2) != 'A' ||
                frame.GetSingle(3) != 1.25f)
            {
                throw new InvalidOperationException("Narrow callback arguments did not roundtrip.");
            }

            return UmkaValue.From(42);
        });
        configured.Register("enumRoundtrip", frame =>
        {
            if (frame.GetEnum<PackageMode>(0) != PackageMode.Remove)
                throw new InvalidOperationException("Enum callback argument did not roundtrip.");

            return UmkaValue.FromEnum(PackageMode.Select);
        });
        configured.Register("scalarRoundtrip", frame =>
        {
            if (frame.GetScalar<int>(0) != -42 ||
                frame.GetScalar<ulong>(1) != 42UL ||
                frame.GetScalar<double>(2) != 12.25 ||
                !frame.GetScalar<bool>(3) ||
                frame.GetScalar<char>(4) != 'A' ||
                frame.GetScalar<string>(5) != "value" ||
                frame.GetScalar<IntPtr>(6) != IntPtr.Zero ||
                frame.GetScalar<PackageMode>(7) != PackageMode.Select ||
                frame.GetScalar<UmkaValue>(0).AsInt64() != -42)
            {
                throw new InvalidOperationException("Generic scalar callback arguments did not roundtrip.");
            }

            return UmkaValue.FromScalar(42);
        });
    });

using var rules = runtime.CreateHostHandle(7L);
if (runtime.TryGetFunction("missing", out _))
    throw new InvalidOperationException("Unexpected optional function was found.");
if (!runtime.TryGetFunction("addFee", "math.um", out var addFee))
    throw new InvalidOperationException("Expected module function was not found.");
if (runtime.TryGetFunction("missing", "math.um", out _))
    throw new InvalidOperationException("Unexpected optional module function was found.");
var exportVisibility = VerifyExportVisibility(runtime);

var hasUnusedWarning = warnings.Any(w =>
    w.Code == 0 &&
    w.Message?.Contains("not used", StringComparison.Ordinal) == true);
if (!hasUnusedWarning)
    throw new InvalidOperationException("Expected an Umka compile warning for an unused identifier.");

var scoreFunction = runtime.GetFunction("score");
var rangeFunction = runtime.GetFunction("range");
var pairMetadata = scoreFunction.ParameterTypes[1];
var rangeMetadata = rangeFunction.ResultType;
if (pairMetadata.Kind != UmkaTypeKind.Struct ||
    pairMetadata.NativeSize != Marshal.SizeOf<Pair>() ||
    pairMetadata.HasReferences)
{
    throw new InvalidOperationException("Unexpected package consumer struct parameter metadata.");
}

if (rangeMetadata.Kind != UmkaTypeKind.StaticArray ||
    rangeMetadata.NativeSize != Marshal.SizeOf<RealRange>() ||
    rangeMetadata.ItemCount != 2 ||
    rangeMetadata.HasReferences)
{
    throw new InvalidOperationException("Unexpected package consumer static-array result metadata.");
}

var score = scoreFunction.CallInt64(
    UmkaValue.FromHostHandle(rules),
    UmkaValue.FromStruct(new Pair { X = 19, Y = 16 }));
var hostBonus = runtime.GetFunction("echoHandle").CallHostObject<long>(UmkaValue.FromHostHandle(rules));
if (hostBonus != 7L)
    throw new InvalidOperationException("Runtime host-handle result resolution did not roundtrip.");
if (!runtime.GetFunction("echoHandle").TryCallHostObject<long>(out var tryCalledHostBonus, UmkaValue.FromHostHandle(rules)) ||
    tryCalledHostBonus != 7L)
{
    throw new InvalidOperationException("Function try host-handle result resolution did not roundtrip.");
}

if (runtime.GetFunction("echoHandle").TryCallHostObject<long>(out _, UmkaValue.FromPointer(IntPtr.Zero)))
    throw new InvalidOperationException("Unexpected function host-handle result resolution for null pointer.");
var hostPointer = runtime.GetFunction("echoHandle").CallPointer(UmkaValue.FromHostHandle(rules));
if (!runtime.TryGetHostObject<long>(hostPointer, out var tryHostBonus) || tryHostBonus != 7L)
    throw new InvalidOperationException("Runtime try host-handle result resolution did not roundtrip.");
if (runtime.TryGetHostObject<long>(IntPtr.Zero, out _))
    throw new InvalidOperationException("Unexpected runtime host-handle resolution for null pointer.");
var tryHandle = runtime.GetFunction("tryHandle").CallInt64(
    UmkaValue.FromHostHandle(rules),
    UmkaValue.FromPointer(IntPtr.Zero));
if (tryHandle != 7L)
    throw new InvalidOperationException("Callback try host-handle resolution did not roundtrip.");

var range = rangeFunction.CallStruct<RealRange>(
    UmkaValue.From(10.0),
    UmkaValue.From(2.5));
var values = runtime.GetFunction("values").CallArray<long>(3);
var scalarInputs = runtime.GetFunction("scalarInputs").CallScalar<int>(
    UmkaValue.FromScalar(-8),
    UmkaValue.FromScalar(50UL),
    UmkaValue.FromScalar(12.25),
    UmkaValue.FromScalar(true),
    UmkaValue.FromScalar('A'),
    UmkaValue.FromScalar("value"),
    UmkaValue.FromScalar(IntPtr.Zero),
    UmkaValue.FromScalar(PackageMode.Select));
var scalarCallback = runtime.GetFunction("scalarCallback").CallScalar<int>();
var scalarMode = runtime.GetFunction("scalarMode").CallScalar<PackageMode>();
var narrow = runtime.GetFunction("narrow").CallInt64();
var mode = runtime.GetFunction("enumProbe").CallEnum<PackageMode>();
var letter = runtime.GetFunction("letter").CallChar();
var single = runtime.GetFunction("single").CallSingle();
var module = addFee.CallInt64(UmkaValue.From(40L), UmkaValue.From(2L));
var arguments = runtime.GetFunction("argumentSummary").CallString();
if (arguments != "package-consumer.um:alpha:beta")
    throw new InvalidOperationException($"Unexpected command argument summary '{arguments}'.");
var dynamicAnswer = runtime.GetFunction("warningProbe").CallValue().AsInt64();
var dynamicArguments = runtime.GetFunction("argumentSummary").CallValue().AsString();
var dynamicTruth = runtime.GetFunction("truth").CallValue().AsBoolean();
var dynamicRatio = runtime.GetFunction("ratio").CallValue().AsDouble();
var dynamicPointer = runtime.GetFunction("nullHandle").CallValue().AsPointer();
var weakPointerHandle = runtime.GetFunction("weakPointerValue").CallWeakPointer();
var dynamicWeakPointer = runtime.GetFunction("weakPointerValue").CallValue();
var dynamicVoid = runtime.GetFunction("tick").CallValue();
if (dynamicAnswer != 42 ||
    dynamicArguments != arguments ||
    !dynamicTruth ||
    dynamicRatio != 2.5 ||
    dynamicPointer != IntPtr.Zero ||
    dynamicWeakPointer.Kind != UmkaValueKind.WeakPointer ||
    dynamicWeakPointer.AsWeakPointer() != weakPointerHandle ||
    dynamicVoid.Kind != UmkaValueKind.Void)
{
    throw new InvalidOperationException("Dynamic CallValue results did not roundtrip.");
}
if (runtime.GetFunction("takeWeakPointer").CallInt64(UmkaValue.FromWeakPointer(weakPointerHandle)) != 0)
    throw new InvalidOperationException("Weak pointer argument did not roundtrip.");

var negativeValidation = VerifyManagedValidation(scoreFunction, rangeFunction, runtime.GetFunction("values"));
var deferredBoundary = VerifyDeferredBoundary(runtime);
var stringBoundary = VerifyStringBoundaryValidation(runtime);
var fileSystem = VerifyFileSystemOption();
using var retainedClosure = runtime.GetFunction("closureValue").CallNativeValue();
var callable = retainedClosure.AsCallable();
if (!retainedClosure.IsCallable ||
    !retainedClosure.Type.IsCallable ||
    !callable.IsRetainedCallable ||
    callable.ResultType.Kind != UmkaTypeKind.SignedInteger)
{
    throw new InvalidOperationException("Retained closure callable metadata did not roundtrip.");
}

var callableAnswer = callable.CallInt64();
if (callableAnswer != 42)
    throw new InvalidOperationException("Retained closure callable invocation did not roundtrip.");

Console.WriteLine($"score={score}");
Console.WriteLine($"host={hostBonus}");
Console.WriteLine($"try-host={tryHostBonus}:{tryHandle}");
Console.WriteLine(FormattableString.Invariant($"range={range.Low:0.0}:{range.High:0.0}"));
Console.WriteLine($"metadata=pair:{pairMetadata.NativeSize}:range:{rangeMetadata.NativeSize}:items:{rangeMetadata.ItemCount}:norefs");
Console.WriteLine($"values={values[0] + values[1] + values[2]}");
Console.WriteLine($"scalar={scalarInputs}:{scalarCallback}:{(byte)scalarMode}");
Console.WriteLine($"narrow={narrow}");
Console.WriteLine($"enum={(byte)mode}");
Console.WriteLine($"letter={letter}");
Console.WriteLine(FormattableString.Invariant($"single={single:0.00}"));
Console.WriteLine($"module={module}");
Console.WriteLine($"exports={exportVisibility}");
Console.WriteLine($"args={arguments}");
Console.WriteLine(FormattableString.Invariant(
    $"dynamic={dynamicAnswer}:{dynamicArguments}:{dynamicTruth}:{dynamicRatio:0.0}:zero:{dynamicVoid.Kind}"));
Console.WriteLine($"negative={negativeValidation}");
Console.WriteLine($"deferred={deferredBoundary}");
Console.WriteLine($"callable={callableAnswer}:{callable.IsRetainedCallable}");
Console.WriteLine($"strings={stringBoundary}");
Console.WriteLine("warning=not-used");
Console.WriteLine($"fs={fileSystem}");

static string VerifyManagedValidation(
    UmkaFunction scoreFunction,
    UmkaFunction rangeFunction,
    UmkaFunction valuesFunction)
{
    ExpectThrows<ArgumentException>(() =>
        scoreFunction.CallInt64(UmkaValue.FromPointer(IntPtr.Zero), UmkaValue.From(42L)));
    ExpectThrows<InvalidOperationException>(() => rangeFunction.CallValue());
    ExpectThrows<InvalidOperationException>(() => valuesFunction.CallArray<long>(2));

    return "managed-validation";
}

static string VerifyDeferredBoundary(UmkaRuntime runtime)
{
    var argumentExpectations = new (string FunctionName, UmkaTypeKind Kind)[]
    {
        ("takeDynamicArray", UmkaTypeKind.DynamicArray),
        ("takeMap", UmkaTypeKind.Map),
        ("takeInterface", UmkaTypeKind.Interface),
        ("takeAny", UmkaTypeKind.Interface),
        ("takeClosure", UmkaTypeKind.Closure),
        ("takeFiber", UmkaTypeKind.Fiber),
    };

    var resultExpectations = new (string FunctionName, UmkaTypeKind Kind)[]
    {
        ("dynamicArrayValue", UmkaTypeKind.DynamicArray),
        ("mapValue", UmkaTypeKind.Map),
        ("interfaceValue", UmkaTypeKind.Interface),
        ("closureValue", UmkaTypeKind.Closure),
        ("fiberValue", UmkaTypeKind.Fiber),
    };

    foreach (var (functionName, expectedKind) in argumentExpectations)
    {
        var function = runtime.GetFunction(functionName);
        if (function.ParameterTypes.Count != 1 || function.ParameterTypes[0].Kind != expectedKind)
            throw new InvalidOperationException($"Unexpected package consumer parameter metadata for '{functionName}'.");

        ExpectThrows<ArgumentException>(() => function.CallInt64(UmkaValue.FromPointer(IntPtr.Zero)));
    }

    foreach (var (functionName, expectedKind) in resultExpectations)
    {
        var function = runtime.GetFunction(functionName);
        if (function.ResultType.Kind != expectedKind)
            throw new InvalidOperationException($"Unexpected package consumer result metadata for '{functionName}'.");

        ExpectThrows<InvalidOperationException>(() => function.CallValue());
    }

    var anyFunction = runtime.GetFunction("anyValue");
    if (anyFunction.ResultType.Kind != UmkaTypeKind.Interface || !anyFunction.ResultType.IsAny)
        throw new InvalidOperationException("Unexpected package consumer any result metadata.");
    if (anyFunction.CallAny().Payload.AsInt64() != 42)
        throw new InvalidOperationException("Package consumer any result did not roundtrip.");

    return
        $"args:{string.Join(':', argumentExpectations.Select(item => item.Kind))};" +
        $"results:{string.Join(':', resultExpectations.Select(item => item.Kind))};" +
        "any:supported";
}

static string VerifyExportVisibility(UmkaRuntime runtime)
{
    if (runtime.GetFunction("exportProbe").CallInt64() != 42)
        throw new InvalidOperationException("Exported root function lookup did not roundtrip.");

    if (runtime.TryGetFunction("hiddenPackageValue", out _))
        throw new InvalidOperationException("Unexpected non-exported root function lookup succeeded.");

    if (runtime.TryGetFunction("hiddenAddFee", "math.um", out _))
        throw new InvalidOperationException("Unexpected non-exported module function lookup succeeded.");

    ExpectThrows<UmkaException>(() => runtime.GetFunction("hiddenPackageValue"));
    ExpectThrows<UmkaException>(() => runtime.GetFunction("hiddenAddFee", "math.um"));

    return "hidden-blocked";
}

static string VerifyStringBoundaryValidation(UmkaRuntime runtime)
{
    ExpectThrows<ArgumentException>(() => UmkaValue.From("bad\0value"));
    ExpectThrows<ArgumentException>(() => UmkaRuntime.FromSource("fn main() {}\0trailing"));
    ExpectThrows<ArgumentException>(() =>
        UmkaRuntime.FromSource("fn main() {}", arguments: ["package-consumer.um", "bad\0argument"]));
    ExpectThrows<ArgumentException>(() => runtime.GetFunction("score\0trailing"));

    return "nul-boundary";
}

static void ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} was not thrown.");
}

static string VerifyFileSystemOption()
{
    var tempDir = Path.Combine(Path.GetTempPath(), "UmkaSharp.PackageConsumer", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDir);
    var outputFile = Path.Combine(tempDir, "file-system-enabled.txt");
    try
    {
        using (var sandboxed = UmkaRuntime.CompileSource(FileWriterSource))
        {
            var sandboxedResult = sandboxed.GetFunction("writeText").CallInt64(UmkaValue.From(outputFile));
            if (sandboxedResult == 0 || File.Exists(outputFile))
                throw new InvalidOperationException("Default file-system sandbox unexpectedly allowed writing.");
        }

        using var enabled = UmkaRuntime.CompileSource(
            FileWriterSource,
            new UmkaRuntimeOptions
            {
                FileSystemEnabled = true,
            });

        var enabledResult = enabled.GetFunction("writeText").CallInt64(UmkaValue.From(outputFile));
        if (enabledResult != 0)
            throw new InvalidOperationException($"File-system-enabled runtime returned error code {enabledResult}.");

        var contents = File.ReadAllText(outputFile);
        if (contents != "Hello from package")
            throw new InvalidOperationException($"Unexpected file contents: '{contents}'.");

        return "sandboxed-enabled";
    }
    finally
    {
        if (Directory.Exists(tempDir))
            Directory.Delete(tempDir, recursive: true);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct Pair
{
    public long X;
    public long Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RealRange
{
    public double Low;
    public double High;
}

internal enum PackageMode : byte
{
    Draw = 74,
    Select,
    Remove = 8,
    Edit
}
