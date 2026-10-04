using System.Text.RegularExpressions;

namespace CMToolkit.ParityCoverage;

/// <summary>Reads the checklist IDs out of <c>docs/parity-inventory.md</c>.</summary>
public static partial class ParityInventory
{
    /// <summary>
    /// Every ID in the first column of each Markdown table whose first header is <c>ID</c>, in document order. A cell
    /// may hold several IDs: a comma list whose later items inherit the first one's prefix (<c>OVW-P5, P6</c>), an
    /// en-dash range (<c>OVW-P15–P18</c>), or an ID followed by a <c>·</c> and an issue link (<c>B-1 · [#20](…)</c>).
    /// A cell of <c>—</c> is a row without an ID.
    /// </summary>
    /// <exception cref="FormatException">A cell isn't one of those forms, or an ID appears twice.</exception>
    public static IReadOnlyList<string> ParseIds(string markdown)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inIdTable = false;
        var previousWasTableRow = false;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith('|'))
            {
                inIdTable = false;
                previousWasTableRow = false;
                continue;
            }

            var firstCell = line.Split('|', StringSplitOptions.None)[1].Trim();
            if (!previousWasTableRow)
            {
                // A header row: the table is an ID table if its first header is "ID".
                inIdTable = firstCell == "ID";
                previousWasTableRow = true;
                continue;
            }

            if (!inIdTable || SeparatorRow().IsMatch(firstCell) || firstCell == "—")
            {
                continue;
            }

            foreach (var id in ExpandCell(firstCell))
            {
                if (!seen.Add(id))
                {
                    throw new FormatException($"The Parity Inventory lists {id} twice.");
                }

                ids.Add(id);
            }
        }

        return ids;
    }

    private static IEnumerable<string> ExpandCell(string cell)
    {
        var text = cell.Split('·', 2)[0].Trim();
        string? prefix = null;
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            var range = part.Split('–', StringSplitOptions.TrimEntries);
            var first = ParseId(range[0], prefix, cell);
            prefix = first.Prefix;
            if (range.Length == 1)
            {
                yield return first.ToString();
                continue;
            }

            var last = ParseId(range[1], prefix, cell);
            if (range.Length > 2 || last.Letters != first.Letters || last.Number < first.Number)
            {
                throw new FormatException($"Malformed ID range in the Parity Inventory: '{cell}'.");
            }

            for (var n = first.Number; n <= last.Number; n++)
            {
                yield return (first with { Number = n }).ToString();
            }
        }
    }

    private static Id ParseId(string text, string? inheritedPrefix, string cell)
    {
        var full = FullId().Match(text);
        if (full.Success)
        {
            return new Id(full.Groups["prefix"].Value, full.Groups["letters"].Value, int.Parse(full.Groups["number"].Value));
        }

        var shorthand = ShorthandId().Match(text);
        if (shorthand.Success && inheritedPrefix is not null)
        {
            return new Id(inheritedPrefix, shorthand.Groups["letters"].Value, int.Parse(shorthand.Groups["number"].Value));
        }

        throw new FormatException($"Malformed ID cell in the Parity Inventory: '{cell}'.");
    }

    private sealed record Id(string Prefix, string Letters, int Number)
    {
        public override string ToString() => $"{Prefix}-{Letters}{Number}";
    }

    /// <summary><c>SHELL-1</c>, <c>OVW-P15</c>, <c>MO2-3</c>, <c>F4SE-7</c>, <c>T-4</c>.</summary>
    [GeneratedRegex(@"^(?<prefix>[A-Z][A-Z0-9]*)-(?<letters>[A-Z]*)(?<number>\d+)$")]
    private static partial Regex FullId();

    /// <summary>The <c>P6</c> in <c>OVW-P5, P6</c>.</summary>
    [GeneratedRegex(@"^(?<letters>[A-Z]*)(?<number>\d+)$")]
    private static partial Regex ShorthandId();

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex SeparatorRow();
}
