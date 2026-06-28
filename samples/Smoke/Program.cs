using UmkaSharp;

using var runtime = UmkaRuntime.FromSource("""
    import "host.um"

    fn answer*(): int {
        return host::doubleIt(21)
    }
    """);

runtime.AddModule("host.um", "fn doubleIt*(x: int): int");
runtime.Register("doubleIt", frame => UmkaValue.From(frame.GetInt64(0) * 2));
runtime.Compile();

var answer = runtime.GetFunction("answer").CallInt64();
Console.WriteLine(answer);
