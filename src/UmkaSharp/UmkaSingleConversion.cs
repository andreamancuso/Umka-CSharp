using System.Globalization;

namespace UmkaSharp;

internal static class UmkaSingleConversion
{
    public static string FiniteRangeDescription { get; } =
        $"{Format(-(double)float.MaxValue)}..{Format(float.MaxValue)}";

    public static float ToSingleChecked(double value, string valueDescription)
    {
        if (IsOutsideFiniteSingleRange(value))
        {
            throw new OverflowException(
                $"{valueDescription} {Format(value)} is outside the System.Single finite range {FiniteRangeDescription}.");
        }

        return (float)value;
    }

    public static bool IsOutsideFiniteSingleRange(double value) =>
        double.IsFinite(value) && (value < -float.MaxValue || value > float.MaxValue);

    public static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
