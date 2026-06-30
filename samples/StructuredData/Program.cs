using System.Runtime.InteropServices;
using UmkaSharp;

using var runtime = UmkaRuntime.CompileSource("""
    type Point = struct {
        x, y: real
    }

    fn range*(center, radius: real): [2]real {
        return [2]real{center - radius, center + radius}
    }

    fn distanceSquared*(point: Point): real {
        return point.x * point.x + point.y * point.y
    }
    """);

var range = runtime.GetFunction("range").CallStruct<RealRange>(
    UmkaValue.From(10.0),
    UmkaValue.From(2.5));

Console.WriteLine(FormattableString.Invariant($"Range: {range.Low:0.0} - {range.High:0.0}"));

var distanceSquared = runtime.GetFunction("distanceSquared").CallDouble(
    UmkaValue.FromStruct(new Point { X = 3.0, Y = 4.0 }));

Console.WriteLine(FormattableString.Invariant($"Distance squared: {distanceSquared:0.0}"));

[StructLayout(LayoutKind.Sequential)]
public struct RealRange
{
    public double Low;
    public double High;
}

[StructLayout(LayoutKind.Sequential)]
public struct Point
{
    public double X;
    public double Y;
}
