using System.Numerics;
using System.Text.RegularExpressions;

namespace CMToolkit.Core.Updates;

/// <summary>A version string <c>packaging.version.Version</c> refuses (its <c>InvalidVersion</c>).</summary>
public sealed class InvalidVersionException(string version)
    : FormatException($"Invalid version: '{version}'");

/// <summary>
/// <c>packaging.version.Version</c> 25.0, the PEP 440 comparer behind the update check (UPD-3, T-4): the same accepted
/// spellings (a leading <c>v</c>, <c>-</c>/<c>_</c>/<c>.</c> separators, <c>alpha</c>/<c>beta</c>/<c>c</c>/<c>pre</c>/
/// <c>preview</c>/<c>rev</c>/<c>r</c>, implicit post releases, surrounding whitespace) and the same ordering
/// (<c>1.2 == 1.2.0</c>, dev releases first, post releases last, local segments after the public version).
/// </summary>
/// <remarks>
/// Only comparison is ported: the update check never prints a parsed version, only the text it parsed.
/// </remarks>
public sealed partial class Pep440Version : IComparable<Pep440Version>
{
    /// <summary>
    /// Python 3.14's <c>sys.get_int_max_str_digits()</c>: <c>int()</c> refuses a longer digit string with
    /// <c>ValueError</c>, which is not <c>InvalidVersion</c>.
    /// </summary>
    private const int MaxIntDigits = 4300;

    private readonly BigInteger _epoch;
    private readonly BigInteger[] _release;
    private readonly Segment _pre;
    private readonly Segment _post;
    private readonly Segment _dev;
    private readonly LocalPart[]? _local;

    private Pep440Version(BigInteger epoch, BigInteger[] release, Segment pre, Segment post, Segment dev, LocalPart[]? local)
    {
        _epoch = epoch;
        _release = release;
        _pre = pre;
        _post = post;
        _dev = dev;
        _local = local;
    }

    /// <summary>
    /// <c>Version(text)</c>.
    /// </summary>
    /// <exception cref="InvalidVersionException">The text isn't a PEP 440 version (<c>InvalidVersion</c>).</exception>
    /// <exception cref="FormatException">
    /// A number in it is longer than Python's 4300-digit <c>int()</c> limit: Python raises a plain <c>ValueError</c>,
    /// which the update check doesn't catch.
    /// </exception>
    public static Pep440Version Parse(string text)
    {
        var match = VersionPattern().Match(text);
        if (!match.Success)
        {
            throw new InvalidVersionException(text);
        }

        var epoch = match.Groups["epoch"].Success ? Int(match.Groups["epoch"].Value) : BigInteger.Zero;
        var release = match.Groups["release"].Value.Split('.').Select(Int).ToArray();
        var pre = Letter(match.Groups["pre_l"], match.Groups["pre_n"].Success ? match.Groups["pre_n"].Value : null);
        var postNumber = match.Groups["post_n1"].Success ? match.Groups["post_n1"].Value
            : match.Groups["post_n2"].Success ? match.Groups["post_n2"].Value : null;
        var post = Letter(match.Groups["post_l"], postNumber);
        var dev = Letter(match.Groups["dev_l"], match.Groups["dev_n"].Success ? match.Groups["dev_n"].Value : null);
        var local = match.Groups["local"].Success ? LocalParts(match.Groups["local"].Value) : null;

        // _cmpkey: 1.0.dev0 sorts before 1.0a0 by standing in for the missing pre-release, but only when there is no
        // pre or post segment; otherwise a missing pre-release sorts after every pre-release.
        var preKey = pre ?? (post is null && dev is not null ? Segment.NegativeInfinity : Segment.Infinity);
        return new Pep440Version(epoch, release, preKey, post ?? Segment.NegativeInfinity, dev ?? Segment.Infinity, local);
    }

    /// <summary>
    /// <c>Version(latest) &gt; Version(str(APP_VERSION))</c> (UPD-3): whether <paramref name="latest"/> is newer than
    /// the running <see cref="AppInfo.Version"/>.
    /// </summary>
    /// <exception cref="InvalidVersionException"><paramref name="latest"/> isn't a PEP 440 version.</exception>
    public static bool IsNewerThanRunning(string latest) => Parse(latest).CompareTo(Parse(AppInfo.Version)) > 0;

    /// <inheritdoc/>
    public int CompareTo(Pep440Version? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = _epoch.CompareTo(other._epoch);
        if (result == 0)
        {
            result = CompareRelease(_release, other._release);
        }

        if (result == 0)
        {
            result = _pre.CompareTo(other._pre);
        }

        if (result == 0)
        {
            result = _post.CompareTo(other._post);
        }

        if (result == 0)
        {
            result = _dev.CompareTo(other._dev);
        }

        return result == 0 ? CompareLocal(_local, other._local) : result;
    }

    /// <summary>
    /// The release tuples with their trailing zeros dropped, compared element by element; a prefix sorts first.
    /// </summary>
    private static int CompareRelease(BigInteger[] left, BigInteger[] right)
    {
        var leftLength = TrimmedLength(left);
        var rightLength = TrimmedLength(right);
        for (var i = 0; i < Math.Min(leftLength, rightLength); i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return leftLength.CompareTo(rightLength);

        static int TrimmedLength(BigInteger[] release)
        {
            var length = release.Length;
            while (length > 0 && release[length - 1].IsZero)
            {
                length--;
            }

            return length;
        }
    }

    /// <summary>
    /// No local segment sorts first. Otherwise the parts compare in order: a text part before a number, texts
    /// ordinally, numbers numerically; a prefix sorts first.
    /// </summary>
    private static int CompareLocal(LocalPart[]? left, LocalPart[]? right)
    {
        if (left is null || right is null)
        {
            return (left is null ? 0 : 1) - (right is null ? 0 : 1);
        }

        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var result = left[i].CompareTo(right[i]);
            if (result != 0)
            {
                return result;
            }
        }

        return left.Length.CompareTo(right.Length);
    }

    /// <summary>
    /// <c>_parse_letter_version</c>: a letter (lowercased, alternative spellings normalised) with its number, 0 when
    /// absent; or, with no letter, the implicit post release <c>1.0-1</c>; or nothing.
    /// </summary>
    private static Segment? Letter(Group letterGroup, string? number)
    {
        if (letterGroup.Success && letterGroup.Value.Length > 0)
        {
            var letter = PythonLower(letterGroup.Value) switch
            {
                "alpha" => "a",
                "beta" => "b",
                "c" or "pre" or "preview" => "rc",
                "rev" or "r" => "post",
                var other => other,
            };
            return new Segment(letter, number is null ? BigInteger.Zero : Int(number));
        }

        // `if number:` on the text: any digit string, "0" included, is truthy.
        return string.IsNullOrEmpty(number) ? null : new Segment("post", Int(number));
    }

    /// <summary><c>_parse_local_version</c>: split on <c>.</c>, <c>_</c> and <c>-</c>; digit parts are numbers.</summary>
    private static LocalPart[] LocalParts(string local) =>
    [
        .. local.Split('.', '_', '-').Select(part => part.All(char.IsAsciiDigit)
            ? new LocalPart(null, Int(part))
            : new LocalPart(PythonLower(part), BigInteger.Zero)),
    ];

    /// <summary>Python's <c>int()</c> of an ASCII digit string, with its digit limit.</summary>
    private static BigInteger Int(string digits)
    {
        if (digits.Length > MaxIntDigits)
        {
            throw new FormatException(
                $"Exceeds the limit ({MaxIntDigits} digits) for integer string conversion: value has {digits.Length} digits");
        }

        return BigInteger.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>str.lower()</c> over the characters the pattern can match: ASCII letters, and the four non-ASCII letters
    /// Python's <c>IGNORECASE</c> folds onto ASCII ones. Only U+0130 changes length (to <c>i</c> plus a combining dot);
    /// U+0131 and U+017F are already lowercase, and the Kelvin sign lowers to <c>k</c>.
    /// </summary>
    private static string PythonLower(string text) => text
        .Replace("\u0130", "i\u0307", StringComparison.Ordinal)
        .Replace('\u212A', 'k')
        .ToLowerInvariant();

    /// <summary>
    /// <c>Version._regex</c>: <c>^\s*VERSION_PATTERN\s*$</c> with <c>re.VERBOSE | re.IGNORECASE</c>. The case
    /// insensitivity is spelled out instead of using <see cref="RegexOptions.IgnoreCase"/>, because Python's folds
    /// differ from .NET's: under Python, <c>i</c> also matches U+0130 and U+0131, <c>s</c> matches U+017F, and
    /// <c>[a-z]</c> also matches those three and the Kelvin sign. <c>\s</c> is Python's whitespace (.NET's plus
    /// U+001C..U+001F), <c>$</c> also matches before a final newline, and the digits are ASCII only.
    /// </summary>
    [GeneratedRegex(
        """
        ^[\s\x1C-\x1F]*
        [vV]?
        (?:
            (?:(?<epoch>[0-9]+)!)?
            (?<release>[0-9]+(?:\.[0-9]+)*)
            (?<pre>
                [-_.]?
                (?<pre_l>[aA][lL][pP][hH][aA]|[aA]|[bB][eE][tT][aA]|[bB]|[pP][rR][eE][vV][iI\u0130\u0131][eE][wW]|[pP][rR][eE]|[cC]|[rR][cC])
                [-_.]?
                (?<pre_n>[0-9]+)?
            )?
            (?<post>
                (?:-(?<post_n1>[0-9]+))
                |
                (?:
                    [-_.]?
                    (?<post_l>[pP][oO][sS\u017F][tT]|[rR][eE][vV]|[rR])
                    [-_.]?
                    (?<post_n2>[0-9]+)?
                )
            )?
            (?<dev>
                [-_.]?
                (?<dev_l>[dD][eE][vV])
                [-_.]?
                (?<dev_n>[0-9]+)?
            )?
        )
        (?:\+(?<local>[a-zA-Z0-9\u0130\u0131\u017f\u212a]+(?:[-_.][a-zA-Z0-9\u0130\u0131\u017f\u212a]+)*))?
        [\s\x1C-\x1F]*$
        """,
        RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    /// <summary>
    /// One of the pre, post or dev keys: <c>Infinity</c>, <c>NegativeInfinity</c>, or a (letter, number) tuple.
    /// </summary>
    /// <param name="Letter">The normalised letter of a tuple.</param>
    /// <param name="Number">The number of a tuple.</param>
    /// <param name="Rank">1 for <c>Infinity</c>, -1 for <c>NegativeInfinity</c>, 0 for a tuple.</param>
    private sealed record Segment(string? Letter, BigInteger Number, int Rank = 0) : IComparable<Segment>
    {
        public static Segment Infinity { get; } = new(null, BigInteger.Zero, 1);

        public static Segment NegativeInfinity { get; } = new(null, BigInteger.Zero, -1);

        public int CompareTo(Segment? other)
        {
            if (Rank != 0 || other!.Rank != 0)
            {
                return Rank.CompareTo(other!.Rank);
            }

            var result = string.CompareOrdinal(Letter, other.Letter);
            return result != 0 ? Math.Sign(result) : Number.CompareTo(other.Number);
        }
    }

    /// <summary>One local part: <c>(number, "")</c>, or <c>(NegativeInfinity, text)</c> for a text part.</summary>
    private sealed record LocalPart(string? Text, BigInteger Number) : IComparable<LocalPart>
    {
        public int CompareTo(LocalPart? other)
        {
            if ((Text is null) != (other!.Text is null))
            {
                // NegativeInfinity < any int: a text part sorts before a number.
                return Text is null ? 1 : -1;
            }

            return Text is null ? Number.CompareTo(other.Number) : Math.Sign(string.CompareOrdinal(Text, other.Text));
        }
    }
}
