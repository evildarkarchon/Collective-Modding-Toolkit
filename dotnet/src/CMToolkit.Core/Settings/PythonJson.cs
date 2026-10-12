using System.Globalization;
using System.Numerics;
using System.Text;

namespace CMToolkit.Core.Settings;

/// <summary>
/// JSON text that Python's <c>json.loads</c> would reject (or fail to convert): a <c>json.JSONDecodeError</c>, or
/// another <c>ValueError</c> (the int digit limit) or <c>RecursionError</c> (nesting). Every caller treats them alike:
/// <c>AppSettings</c> resets the file, and the update check treats any failure as no update (bug #81, fixed).
/// </summary>
/// <param name="message">Python's message.</param>
/// <param name="position">The character index it points at.</param>
internal sealed class PythonJsonException(string message, int position)
    : FormatException($"{message}: char {position}");

/// <summary>
/// Python's <c>json.loads(str)</c> with its default (C scanner, strict) settings, which <c>settings.json</c> goes
/// through (SET-4, SET-5). <c>System.Text.Json</c> can't stand in for it: Python accepts <c>NaN</c>, <c>Infinity</c>,
/// <c>-Infinity</c>, repeated keys and lone surrogate escapes, reads integers at arbitrary precision, and refuses a
/// leading BOM, and every one of those changes what the reference logs or whether it resets the file.
/// </summary>
internal static class PythonJson
{
    /// <summary>
    /// Python 3.14's limit on the digits of an <c>int</c> parsed from text (<c>sys.get_int_max_str_digits()</c>).
    /// A longer integer literal raises <c>ValueError</c>, which <c>AppSettings</c> treats as an unparseable file.
    /// </summary>
    private const int MaxIntDigits = 4300;

    /// <summary>
    /// How deeply arrays and objects may nest before the parse fails. Python's limit is its C-stack guard, which depends
    /// on the machine; this is a fixed stand-in that keeps a hostile file from overflowing the stack (a crash, where
    /// Python raises <c>RecursionError</c> and resets the settings). Real settings files never nest.
    /// </summary>
    private const int MaxDepth = 1000;

    /// <summary>Parses <paramref name="text"/> as Python's <c>json.loads</c> would.</summary>
    /// <exception cref="PythonJsonException">Python would raise <c>JSONDecodeError</c> or <c>ValueError</c>.</exception>
    public static PythonValue Loads(string text)
    {
        if (text.StartsWith('\uFEFF'))
        {
            throw new PythonJsonException("Unexpected UTF-8 BOM (decode using utf-8-sig)", 0);
        }

        var parser = new Parser(text);
        var (value, end) = parser.ScanOnce(parser.SkipWhitespace(0), 0);
        end = parser.SkipWhitespace(end);
        if (end != text.Length)
        {
            throw new PythonJsonException("Extra data", end);
        }

        return value;
    }

    /// <summary>
    /// CPython's C scanner (<c>Modules/_json.c</c>) over one string <paramref name="s"/>: each method takes the index to
    /// start at and returns the value with the index after it, raising <see cref="PythonJsonException"/> with Python's
    /// message and position.
    /// </summary>
    private sealed class Parser(string s)
    {
        /// <summary>Python's <c>WHITESPACE</c>: <c>[ \t\n\r]*</c>.</summary>
        public int SkipWhitespace(int index)
        {
            while (index < s.Length && s[index] is ' ' or '\t' or '\n' or '\r')
            {
                index++;
            }

            return index;
        }

        /// <summary><c>scan_once</c>: one value starting at <paramref name="index"/>, and the index after it.</summary>
        public (PythonValue Value, int End) ScanOnce(int index, int depth)
        {
            if (index >= s.Length)
            {
                throw new PythonJsonException("Expecting value", index);
            }

            switch (s[index])
            {
                case '"':
                    var (text, end) = ScanString(index + 1);
                    return (new PythonStr(text), end);
                case '{':
                    return ParseObject(index + 1, depth + 1);
                case '[':
                    return ParseArray(index + 1, depth + 1);
                case 'n' when Matches(index, "null"):
                    return (PythonNone.Instance, index + 4);
                case 't' when Matches(index, "true"):
                    return (new PythonBool(true), index + 4);
                case 'f' when Matches(index, "false"):
                    return (new PythonBool(false), index + 5);
                case 'N' when Matches(index, "NaN"):
                    return (new PythonFloat(double.NaN), index + 3);
                case 'I' when Matches(index, "Infinity"):
                    return (new PythonFloat(double.PositiveInfinity), index + 8);
                case '-' when Matches(index, "-Infinity"):
                    return (new PythonFloat(double.NegativeInfinity), index + 9);
                default:
                    return MatchNumber(index);
            }
        }

        private bool Matches(int index, string token) => string.CompareOrdinal(s, index, token, 0, token.Length) == 0;

        private bool IsDigit(int index) => index < s.Length && s[index] is >= '0' and <= '9';

        /// <summary><c>_match_number_unicode</c>: the longest valid prefix, backtracking over a bare <c>e</c>.</summary>
        private (PythonValue Value, int End) MatchNumber(int start)
        {
            var index = start;
            if (s[index] == '-')
            {
                index++;
                if (index >= s.Length)
                {
                    throw new PythonJsonException("Expecting value", start);
                }
            }

            if (s[index] is >= '1' and <= '9')
            {
                index++;
                while (IsDigit(index))
                {
                    index++;
                }
            }
            else if (s[index] == '0')
            {
                index++;
            }
            else
            {
                throw new PythonJsonException("Expecting value", start);
            }

            var isFloat = false;
            if (index < s.Length && s[index] == '.' && IsDigit(index + 1))
            {
                isFloat = true;
                index += 2;
                while (IsDigit(index))
                {
                    index++;
                }
            }

            // s.Length - 1, not s.Length: CPython's _match_number_unicode bounds these two checks by end_idx, the index of
            // the last character, so an 'e' or a sign that is the string's last character isn't taken as an exponent.
            // The number then ends before it, and the caller fails on it as extra data or a missing delimiter.
            if (index < s.Length - 1 && s[index] is 'e' or 'E')
            {
                var exponentStart = index;
                index++;
                if (index < s.Length - 1 && s[index] is '-' or '+')
                {
                    index++;
                }

                while (IsDigit(index))
                {
                    index++;
                }

                if (s[index - 1] is >= '0' and <= '9')
                {
                    isFloat = true;
                }
                else
                {
                    index = exponentStart;
                }
            }

            var literal = s[start..index];
            if (isFloat)
            {
                // Correctly rounded, like Python's float(); out-of-range values become infinities, not errors.
                return (new PythonFloat(double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture)), index);
            }

            var digitCount = literal.Length - (literal[0] == '-' ? 1 : 0);
            if (digitCount > MaxIntDigits)
            {
                throw new PythonJsonException("Exceeds the limit (4300 digits) for integer string conversion", start);
            }

            return (new PythonInt(BigInteger.Parse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)), index);
        }

        /// <summary>
        /// <c>scanstring</c> in strict mode, from just after the opening quote: control characters are refused, and a
        /// <c>\uD8xx\uDCxx</c> escape pair is joined while a lone surrogate escape is kept as it is.
        /// </summary>
        private (string Text, int End) ScanString(int index)
        {
            var start = index - 1;
            var builder = new StringBuilder();
            while (true)
            {
                if (index >= s.Length)
                {
                    throw new PythonJsonException("Unterminated string starting at", start);
                }

                var c = s[index];
                if (c == '"')
                {
                    return (builder.ToString(), index + 1);
                }

                if (c < 0x20)
                {
                    throw new PythonJsonException("Invalid control character at", index);
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    index++;
                    continue;
                }

                index++;
                if (index >= s.Length)
                {
                    throw new PythonJsonException("Unterminated string starting at", start);
                }

                var escape = s[index];
                if (escape != 'u')
                {
                    builder.Append(escape switch
                    {
                        '"' => '"',
                        '\\' => '\\',
                        '/' => '/',
                        'b' => '\b',
                        'f' => '\f',
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        _ => throw new PythonJsonException("Invalid \\escape", index - 1),
                    });
                    index++;
                    continue;
                }

                var unit = ReadHex4(index + 1);
                index += 5;
                if (char.IsHighSurrogate(unit) && index + 1 < s.Length && s[index] == '\\' && s[index + 1] == 'u')
                {
                    var low = ReadHex4(index + 2);
                    if (char.IsLowSurrogate(low))
                    {
                        builder.Append(unit).Append(low);
                        index += 6;
                        continue;
                    }
                }

                builder.Append(unit);
            }
        }

        /// <summary>The four hex digits of a <c>\u</c> escape at <paramref name="index"/>.</summary>
        private char ReadHex4(int index)
        {
            if (index + 4 > s.Length)
            {
                throw new PythonJsonException("Invalid \\uXXXX escape", index - 1);
            }

            var value = 0;
            for (var i = index; i < index + 4; i++)
            {
                var digit = s[i] switch
                {
                    >= '0' and <= '9' => s[i] - '0',
                    >= 'a' and <= 'f' => s[i] - 'a' + 10,
                    >= 'A' and <= 'F' => s[i] - 'A' + 10,
                    _ => throw new PythonJsonException("Invalid \\uXXXX escape", index - 1),
                };
                value = (value << 4) | digit;
            }

            return (char)value;
        }

        /// <summary>
        /// <c>_parse_object</c>: the members of an object whose <c>{</c> ends just before <paramref name="index"/>, in
        /// order, and the index after its <c>}</c>. A repeated key keeps its first position and takes the last value, as
        /// a Python dict built from the pairs does.
        /// </summary>
        /// <param name="index">The index just after the opening brace.</param>
        /// <param name="depth">The object's nesting depth (see <see cref="CheckDepth"/>).</param>
        /// <exception cref="PythonJsonException">The object is malformed or nests too deeply.</exception>
        private (PythonValue Value, int End) ParseObject(int index, int depth)
        {
            CheckDepth(depth, index);
            var items = new List<KeyValuePair<string, PythonValue>>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            index = SkipWhitespace(index);
            if (index < s.Length && s[index] == '}')
            {
                return (new PythonDict(items), index + 1);
            }

            while (true)
            {
                if (index >= s.Length || s[index] != '"')
                {
                    throw new PythonJsonException("Expecting property name enclosed in double quotes", index);
                }

                var (key, afterKey) = ScanString(index + 1);
                index = SkipWhitespace(afterKey);
                if (index >= s.Length || s[index] != ':')
                {
                    throw new PythonJsonException("Expecting ':' delimiter", index);
                }

                var (value, afterValue) = ScanOnce(SkipWhitespace(index + 1), depth);

                // dict semantics: a repeated key keeps its first position and takes the last value.
                if (positions.TryGetValue(key, out var position))
                {
                    items[position] = new KeyValuePair<string, PythonValue>(key, value);
                }
                else
                {
                    positions[key] = items.Count;
                    items.Add(new KeyValuePair<string, PythonValue>(key, value));
                }

                index = SkipWhitespace(afterValue);
                if (index < s.Length && s[index] == '}')
                {
                    return (new PythonDict(items), index + 1);
                }

                if (index >= s.Length || s[index] != ',')
                {
                    throw new PythonJsonException("Expecting ',' delimiter", index);
                }

                index = SkipWhitespace(index + 1);
            }
        }

        /// <summary>
        /// <c>_parse_array</c>: the items of an array whose <c>[</c> ends just before <paramref name="index"/>, and the
        /// index after its <c>]</c>.
        /// </summary>
        /// <param name="index">The index just after the opening bracket.</param>
        /// <param name="depth">The array's nesting depth (see <see cref="CheckDepth"/>).</param>
        /// <exception cref="PythonJsonException">The array is malformed or nests too deeply.</exception>
        private (PythonValue Value, int End) ParseArray(int index, int depth)
        {
            CheckDepth(depth, index);
            var items = new List<PythonValue>();
            index = SkipWhitespace(index);
            if (index < s.Length && s[index] == ']')
            {
                return (new PythonList(items), index + 1);
            }

            while (true)
            {
                var (value, afterValue) = ScanOnce(index, depth);
                items.Add(value);
                index = SkipWhitespace(afterValue);
                if (index < s.Length && s[index] == ']')
                {
                    return (new PythonList(items), index + 1);
                }

                if (index >= s.Length || s[index] != ',')
                {
                    throw new PythonJsonException("Expecting ',' delimiter", index);
                }

                index = SkipWhitespace(index + 1);
            }
        }

        /// <summary>Fails the parse once arrays and objects nest deeper than <see cref="MaxDepth"/>.</summary>
        /// <param name="depth">The nesting depth of the array or object starting here, 1 for the outermost.</param>
        /// <param name="index">Where it starts, for the error.</param>
        /// <exception cref="PythonJsonException">Too deep; Python would raise <c>RecursionError</c>.</exception>
        private static void CheckDepth(int depth, int index)
        {
            if (depth > MaxDepth)
            {
                throw new PythonJsonException("Maximum nesting depth exceeded", index);
            }
        }
    }
}
