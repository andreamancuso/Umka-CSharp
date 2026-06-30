using UmkaSharp;

var records = new List<string>();

using var runtime = UmkaRuntime.CompileSource(
    """
    import "host.um"

    fn report*(name: str, baseScore: int, rules: ^void): str {
        score := host::bonus(baseScore, rules)
        host::record(name, score)
        return host::format(name, score)
    }
    """,
    configure: configured =>
    {
        configured.AddModule("host.um", """
            fn bonus*(score: int, rules: ^void): int
            fn record*(name: str, score: int)
            fn format*(name: str, score: int): str
            """);

        configured.Register("bonus", frame =>
        {
            var bonus = frame.GetHostObject<long>(1);
            return UmkaValue.From(frame.GetInt64(0) + bonus);
        });
        configured.RegisterVoid("record", frame =>
        {
            records.Add($"{frame.GetString(0)}={frame.GetInt64(1)}");
        });
        configured.Register("format", frame =>
            UmkaValue.From($"{frame.GetString(0)} scored {frame.GetInt64(1)}"));
    });

using var rules = runtime.CreateHostHandle(7L);

Console.WriteLine(runtime.GetFunction("report").CallString(
    UmkaValue.From("Ada"),
    UmkaValue.From(35),
    UmkaValue.FromHostHandle(rules)));
Console.WriteLine($"recorded: {string.Join(", ", records)}");
