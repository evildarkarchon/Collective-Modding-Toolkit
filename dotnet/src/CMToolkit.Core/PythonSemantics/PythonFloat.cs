using System.Globalization;
using System.Numerics;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// Python's <c>float</c> rounding and printing (T-3), where the reference turns a number into text: the Overview's
/// HEDR versions (<c>round(struct.unpack("&lt;f", ...)[0], 2)</c>, then <c>str()</c>).
/// </summary>
public static class PythonFloat
{
    /// <summary>
    /// <c>round(value, digits)</c> for a float: the exact binary value rounded to <paramref name="digits"/> decimal places,
    /// half to even on an exact tie, then read back as the nearest double (CPython's <c>double_round</c>, which goes
    /// through a correctly rounded decimal string). Scaling by a power of ten first, as <see cref="Math.Round(double, int)"/>
    /// does, rounds some values the other way: <c>123456.785</c> is just above the tie, so Python gives
    /// <c>123456.79</c>. Infinities and NaN come back unchanged, and a negative value that rounds to zero is <c>-0.0</c>.
    /// </summary>
    /// <param name="value">The float.</param>
    /// <param name="digits">The decimal places, zero or more.</param>
    public static double Round(double value, int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);
        if (!double.IsFinite(value) || value == 0)
        {
            return value;
        }

        // value = mantissa x 2^exponent, exactly.
        var bits = BitConverter.DoubleToInt64Bits(value);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xF_FFFF_FFFF_FFFFL;
        if (exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        exponent -= 1075;
        var numerator = new BigInteger(mantissa) * BigInteger.Pow(10, digits);
        var denominator = BigInteger.One;
        if (exponent > 0)
        {
            numerator <<= exponent;
        }
        else
        {
            denominator <<= -exponent;
        }

        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        var twice = remainder * 2;
        if (twice > denominator || (twice == denominator && !quotient.IsEven))
        {
            quotient += 1;
        }

        // quotient / 10^digits as decimal text, which double.Parse rounds correctly to the nearest double.
        var text = quotient.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        var number = digits == 0 ? text : text[..^digits] + "." + text[^digits..];
        return double.Parse((value < 0 ? "-" : string.Empty) + number, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>str(float)</c> (= <c>repr</c>): the shortest digits that round-trip, always with a decimal point
    /// (<c>1.0</c>, <c>68.0</c>), in exponent notation beyond 1e16 or below 1e-4 (<c>1e+20</c>); <c>inf</c>, <c>nan</c>.
    /// </summary>
    public static string Repr(double value) => Settings.PythonText.FloatRepr(value);
}
