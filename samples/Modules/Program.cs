using UmkaSharp;

using var runtime = UmkaRuntime.CompileSource(
    """
    import "pricing.um"

    fn invoiceTotal*(net: real): real {
        return pricing::withTax(net, 0.23)
    }
    """,
    configure: configured => configured.AddModule("pricing.um", """
        fn withTax*(net, rate: real): real {
            return net * (1.0 + rate)
        }
        """));

var total = runtime.GetFunction("invoiceTotal").CallDouble(UmkaValue.From(120.0));
Console.WriteLine(FormattableString.Invariant($"Invoice total: {total:0.00}"));
