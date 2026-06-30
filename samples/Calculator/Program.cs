using UmkaSharp;

using var runtime = UmkaRuntime.CompileSource("""
    fn subtotal*(unitPrice: real, quantity: int): real {
        return unitPrice * real(quantity)
    }

    fn discount*(amount, percent: real): real {
        return amount * (1.0 - percent / 100.0)
    }
    """);

var subtotal = runtime.GetFunction("subtotal").CallDouble(
    UmkaValue.From(19.95),
    UmkaValue.From(3L));
var total = runtime.GetFunction("discount").CallDouble(
    UmkaValue.From(subtotal),
    UmkaValue.From(10.0));

Console.WriteLine(FormattableString.Invariant($"Subtotal: {subtotal:0.00}"));
Console.WriteLine(FormattableString.Invariant($"Total: {total:0.00}"));
