using System.Text;

namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// Python's <c>str.casefold()</c>: Unicode full case folding, code point by code point, from a table generated from the
/// Reference Implementation's own Python (<c>PythonCaseFold.Table.cs</c>, <c>dotnet/eng/generate-casefold.py</c>).
/// It is not .NET's lowercasing: sharp s (U+00DF) and capital sharp s (U+1E9E) fold to <c>ss</c>, final sigma to sigma, ligatures to their
/// letters. The Archive Patcher's name filter matches on it (AP-2).
/// </summary>
public static partial class PythonCaseFold
{
    /// <summary>
    /// <c>text.casefold()</c>. A lone surrogate, which no table entry names, is kept as it is, as Python keeps one.
    /// </summary>
    public static string Of(string text)
    {
        // ASCII-only text without capitals is already folded; most file names hit this.
        if (!NeedsFolding(text))
        {
            return text;
        }

        var folded = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            var width = 1;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                width = 2;
            }

            var index = Array.BinarySearch(Folded, codePoint);
            if (index >= 0)
            {
                folded.Append(FoldedTo[index]);
            }
            else
            {
                folded.Append(text, i, width);
            }

            i += width - 1;
        }

        return folded.ToString();
    }

    /// <summary>Whether <paramref name="text"/> has anything but lowercase ASCII, digits and ASCII punctuation.</summary>
    private static bool NeedsFolding(string text)
    {
        foreach (var c in text)
        {
            if (c > 0x7F || char.IsAsciiLetterUpper(c))
            {
                return true;
            }
        }

        return false;
    }
}
