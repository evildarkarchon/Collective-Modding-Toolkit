using System.Globalization;
using System.Text;

namespace CMToolkit.Core.Settings;

/// <summary>Python's text renderings of values, where the reference puts them into log messages (T-3, T-9).</summary>
internal static class PythonText
{
    /// <summary>
    /// Python's <c>repr(float)</c> (which <c>str(float)</c> equals): the shortest digits that round-trip, written in
    /// fixed notation with at least one decimal (<c>100.0</c>) unless the decimal point would sit more than 16 places
    /// right or 4 or more places left of the first digit, then as <c>1.5e+20</c> / <c>1e-05</c>. NaN and the
    /// infinities are <c>nan</c>, <c>inf</c> and <c>-inf</c>.
    /// </summary>
    public static string FloatRepr(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        var sign = double.IsNegative(value) ? "-" : string.Empty;
        if (value == 0)
        {
            return sign + "0.0";
        }

        // .NET's "R" is also the shortest round-trip form, but its notation differs (1E+15, no ".0"), so only its
        // digits and exponent are kept: the value is 0.<digits> x 10^decimalPoint.
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = text.IndexOf('E', StringComparison.Ordinal);
        var mantissa = exponentAt < 0 ? text : text[..exponentAt];
        var exponent = exponentAt < 0 ? 0 : int.Parse(text[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var pointAt = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = pointAt < 0 ? mantissa : mantissa.Remove(pointAt, 1);
        var decimalPoint = (pointAt < 0 ? mantissa.Length : pointAt) + exponent;
        var leadingZeros = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leadingZeros..].TrimEnd('0');
        decimalPoint -= leadingZeros;

        // The 'r' rule of CPython's float_repr_style "short" (PyOS_double_to_string).
        if (decimalPoint <= -4 || decimalPoint > 16)
        {
            var power = decimalPoint - 1;
            var head = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{sign}{head}e{(power < 0 ? '-' : '+')}{Math.Abs(power):00}");
        }

        if (decimalPoint <= 0)
        {
            return sign + "0." + new string('0', -decimalPoint) + digits;
        }

        if (decimalPoint >= digits.Length)
        {
            return sign + digits + new string('0', decimalPoint - digits.Length) + ".0";
        }

        return sign + digits[..decimalPoint] + "." + digits[decimalPoint..];
    }

    /// <summary>
    /// Python's <c>repr(str)</c>: single quotes unless the text has a single quote and no double quote; backslash, the
    /// quote, tab, newline and carriage return escaped; other ASCII controls and DEL as <c>\xNN</c>; non-ASCII kept
    /// when printable, else <c>\xNN</c>, <c>\uNNNN</c> or <c>\UNNNNNNNN</c>.
    /// </summary>
    /// <remarks>
    /// "Printable" is Python's <c>str.isprintable</c>: not in a Other (Cc, Cf, Cs, Co, Cn) or Separator (Zl, Zp, Zs)
    /// category. The categories come from .NET's Unicode tables, which can lag or lead Python's by a Unicode version
    /// for newly assigned characters.
    /// </remarks>
    public static string ReprString(string value)
    {
        var quote = value.Contains('\'', StringComparison.Ordinal) && !value.Contains('"', StringComparison.Ordinal) ? '"' : '\'';
        var builder = new StringBuilder(value.Length + 2).Append(quote);
        for (var i = 0; i < value.Length; i++)
        {
            int codePoint = value[i];
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codePoint = char.ConvertToUtf32(value[i], value[i + 1]);
                i++;
            }

            switch (codePoint)
            {
                case '\\':
                    builder.Append(@"\\");
                    break;
                case '\t':
                    builder.Append(@"\t");
                    break;
                case '\n':
                    builder.Append(@"\n");
                    break;
                case '\r':
                    builder.Append(@"\r");
                    break;
                case var c when c == quote:
                    builder.Append('\\').Append(quote);
                    break;
                case < 0x20 or 0x7f:
                    builder.Append(CultureInfo.InvariantCulture, $"\\x{codePoint:x2}");
                    break;
                case < 0x7f:
                    builder.Append((char)codePoint);
                    break;
                default:
                    if (IsPrintable(codePoint))
                    {
                        builder.Append(char.ConvertFromUtf32(codePoint));
                    }
                    else if (codePoint <= 0xff)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\x{codePoint:x2}");
                    }
                    else if (codePoint <= 0xffff)
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\u{codePoint:x4}");
                    }
                    else
                    {
                        builder.Append(CultureInfo.InvariantCulture, $"\\U{codePoint:x8}");
                    }

                    break;
            }
        }

        return builder.Append(quote).ToString();
    }

    /// <summary>Python's <c>str.isprintable</c> for one non-ASCII code point (a lone surrogate is not printable).</summary>
    private static bool IsPrintable(int codePoint)
    {
        if (codePoint is >= 0xd800 and <= 0xdfff)
        {
            return false;
        }

        return CharUnicodeInfo.GetUnicodeCategory(codePoint) switch
        {
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator or UnicodeCategory.SpaceSeparator => false,
            _ => true,
        };
    }
}
