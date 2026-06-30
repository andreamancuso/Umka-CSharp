using UmkaSharp;

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

var answer = runtime.GetFunction("answer").CallInt64();
Console.WriteLine(answer);
