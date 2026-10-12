namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// A <c>pathlib.PureWindowsPath</c> as CPython 3.14 builds it (T-7). Like Python, it keeps the raw string it was made
/// from, joins raw strings with <c>ntpath.join</c> rules, and normalises only when rendered: <c>/</c> becomes <c>\</c>,
/// doubled separators collapse, <c>.</c> segments and a trailing separator go, and <c>..</c> stays. A rooted right-hand
/// side keeps the left side's drive, so <c>Path("D:/MO2") / "/mods"</c> is <c>D:\mods</c> (B-6), where
/// <see cref="Path.Combine(string, string)"/> would give <c>/mods</c>.
/// </summary>
public sealed class PythonPath
{
    private const char Sep = '\\';
    private const char AltSep = '/';

    /// <summary><c>PureWindowsPath(raw)</c>.</summary>
    public PythonPath(string raw)
    {
        Raw = raw;
    }

    /// <summary>The unnormalised string the path was built from, as Python keeps it for later joins.</summary>
    public string Raw { get; }

    /// <summary><c>self / other</c>.</summary>
    public PythonPath Join(string other) => new(NtJoin(Raw, other));

    /// <summary><c>self.parent</c>: the path without its last segment, or the path itself when it has none.</summary>
    public PythonPath Parent
    {
        get
        {
            var (drive, root, tail) = Parse(Raw);
            return tail.Count == 0 ? this : new PythonPath(Format(drive, root, tail[..^1]));
        }
    }

    /// <summary>
    /// <c>self.name</c>: the last segment, or an empty string when there is none (a bare drive or root, or <c>.</c>).
    /// </summary>
    public string Name
    {
        get
        {
            var (_, _, tail) = Parse(Raw);
            return tail.Count == 0 ? string.Empty : tail[^1];
        }
    }

    /// <summary>
    /// <c>self.suffix</c> as of Python 3.14: the last <c>.</c> and what follows it in <see cref="Name"/>, ignoring leading
    /// dots, so <c>.esl</c> alone has none and <c>a.</c> has <c>.</c>; empty without one.
    /// </summary>
    public string Suffix
    {
        get
        {
            var name = Name.TrimStart('.');
            var dot = name.LastIndexOf('.');
            return dot == -1 ? string.Empty : name[dot..];
        }
    }

    /// <summary>
    /// <c>self.stem</c> as of Python 3.14: <see cref="Name"/> without its last suffix, unless that would leave only dots.
    /// </summary>
    public string Stem
    {
        get
        {
            var name = Name;
            var dot = name.LastIndexOf('.');
            return dot != -1 && name[..dot].TrimStart('.').Length > 0 ? name[..dot] : name;
        }
    }

    /// <summary>
    /// <c>PureWindowsPath</c> equality and hashing (T-8): the rendered path compared case-insensitively, as pathlib
    /// compares <c>str(path).lower()</c>. Sets and <c>in</c> tests on reference paths use it.
    /// </summary>
    public static IEqualityComparer<PythonPath> Equality { get; } = new CaseInsensitiveEquality();

    /// <summary><c>self.with_name(name)</c>: the same folder, another last segment.</summary>
    /// <exception cref="ArgumentException">The path has no name, or <paramref name="name"/> isn't one segment.</exception>
    public PythonPath WithName(string name)
    {
        if (name.Length == 0 || name == "." || name.Contains(Sep, StringComparison.Ordinal) || name.Contains(AltSep, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid name '{name}'", nameof(name));
        }

        var (drive, root, tail) = Parse(Raw);
        if (tail.Count == 0)
        {
            throw new ArgumentException($"{this} has an empty name");
        }

        return new PythonPath(Format(drive, root, [.. tail[..^1], name]));
    }

    /// <summary><c>str(path)</c>: the normalised form, or <c>.</c> for an empty path.</summary>
    public override string ToString()
    {
        var (drive, root, tail) = Parse(Raw);
        return Format(drive, root, tail);
    }

    /// <summary>
    /// <c>PureWindowsPath._parse_path</c>: drive, root and the non-empty, non-<c>.</c> segments. A UNC drive with no
    /// root of its own (<c>\\server\share</c>) gets one, as pathlib does.
    /// </summary>
    private static (string Drive, string Root, List<string> Tail) Parse(string raw)
    {
        if (raw.Length == 0)
        {
            return (string.Empty, string.Empty, []);
        }

        var path = raw.Replace(AltSep, Sep);
        var (drive, root, rel) = SplitRoot(path);
        if (root.Length == 0 && drive.StartsWith(Sep) && !drive.EndsWith(Sep))
        {
            var driveParts = drive.Split(Sep);
            // Python's `parts[2] not in '?.'` is a substring test, so an empty server name counts as in it too.
            if ((driveParts.Length == 4 && !"?.".Contains(driveParts[2], StringComparison.Ordinal)) || driveParts.Length == 6)
            {
                root = Sep.ToString();
            }
        }

        var tail = rel.Split(Sep).Where(part => part.Length > 0 && part != ".").ToList();
        return (drive, root, tail);
    }

    /// <summary><c>PureWindowsPath._format_parsed_parts</c>, plus <c>__str__</c>'s <c>or '.'</c>.</summary>
    private static string Format(string drive, string root, List<string> tail)
    {
        if (drive.Length > 0 || root.Length > 0)
        {
            return drive + root + string.Join(Sep, tail);
        }

        if (tail.Count > 0 && SplitRoot(tail[0]).Drive.Length > 0)
        {
            // A first segment that looks like a drive ("c:") is kept relative by prefixing ".".
            tail = [".", .. tail];
        }

        var text = string.Join(Sep, tail);
        return text.Length > 0 ? text : ".";
    }

    /// <summary>
    /// <c>ntpath.splitroot</c>: (drive, root, rest), sliced from <paramref name="p"/> as written. Both separators count.
    /// The drive test is only "a colon second", so <c>1:foo</c> has drive <c>1:</c>, as in Python.
    /// </summary>
    private static (string Drive, string Root, string Remainder) SplitRoot(string p)
    {
        var normp = p.Replace(AltSep, Sep);
        if (normp.StartsWith(Sep))
        {
            if (normp.Length > 1 && normp[1] == Sep)
            {
                // UNC drives (\\server\share, \\?\UNC\server\share) and device drives (\\.\device, \\?\device).
                var start = normp.Length >= 8 && normp[..8].Equals(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase) ? 8 : 2;
                var index = normp.IndexOf(Sep, start);
                if (index == -1)
                {
                    return (p, string.Empty, string.Empty);
                }

                var index2 = normp.IndexOf(Sep, index + 1);
                if (index2 == -1)
                {
                    return (p, string.Empty, string.Empty);
                }

                return (p[..index2], p[index2..(index2 + 1)], p[(index2 + 1)..]);
            }

            // Relative path with a root, e.g. \Windows.
            return (string.Empty, p[..1], p[1..]);
        }

        if (normp.Length > 1 && normp[1] == ':')
        {
            return normp.Length > 2 && normp[2] == Sep
                ? (p[..2], p[2..3], p[3..])
                : (p[..2], string.Empty, p[2..]);
        }

        return (string.Empty, string.Empty, p);
    }

    /// <summary>
    /// <c>ntpath.join(left, right)</c> on raw strings. A right side with a root replaces the left side's root and path
    /// (keeping its drive unless the right side has one); a different drive replaces everything; otherwise the right
    /// side is appended.
    /// </summary>
    private static string NtJoin(string left, string right)
    {
        var (resultDrive, resultRoot, resultPath) = SplitRoot(left);
        var (drive, root, path) = SplitRoot(right);
        if (root.Length > 0)
        {
            if (drive.Length > 0 || resultDrive.Length == 0)
            {
                resultDrive = drive;
            }

            resultRoot = root;
            resultPath = path;
        }
        else if (drive.Length > 0 && !drive.Equals(resultDrive, StringComparison.OrdinalIgnoreCase))
        {
            // Different drives: the left side is ignored entirely.
            resultDrive = drive;
            resultRoot = root;
            resultPath = path;
        }
        else
        {
            if (drive.Length > 0)
            {
                // The same drive, perhaps in a different case: the right side's spelling wins.
                resultDrive = drive;
            }

            if (resultPath.Length > 0 && resultPath[^1] is not (Sep or AltSep))
            {
                resultPath += Sep;
            }

            resultPath += path;
        }

        // A UNC drive followed by a relative path still needs a separator between them.
        if (resultPath.Length > 0 && resultRoot.Length == 0 && resultDrive.Length > 0 && resultDrive[^1] is not (':' or Sep or AltSep))
        {
            return resultDrive + Sep + resultPath;
        }

        return resultDrive + resultRoot + resultPath;
    }

    /// <summary>
    /// <c>_str_normcase</c>: <c>str(self).lower()</c>, with <see cref="string.ToLowerInvariant"/> standing in for
    /// Python's <c>str.lower()</c> (see the README's residuals on lowercasing).
    /// </summary>
    private sealed class CaseInsensitiveEquality : IEqualityComparer<PythonPath>
    {
        public bool Equals(PythonPath? x, PythonPath? y)
            => ReferenceEquals(x, y) || (x is not null && y is not null && string.Equals(Normcase(x), Normcase(y), StringComparison.Ordinal));

        public int GetHashCode(PythonPath obj) => StringComparer.Ordinal.GetHashCode(Normcase(obj));

        private static string Normcase(PythonPath path) => path.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// <c>PurePath.__lt__</c> (Python 3.14): the lowercased rendered paths split on the separator, compared part by part
    /// by code point, a path that is a prefix of another first. <see cref="string.ToLowerInvariant"/> stands in for
    /// <c>str.lower()</c>, as in <see cref="Equality"/>. <c>sorted(paths)</c> uses it.
    /// </summary>
    public static IComparer<PythonPath> Ordering { get; } = new PartsOrdering();

    /// <summary>See <see cref="Ordering"/>.</summary>
    private sealed class PartsOrdering : IComparer<PythonPath>
    {
        public int Compare(PythonPath? x, PythonPath? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null || y is null)
            {
                return x is null ? -1 : 1;
            }

            var left = x.ToString().ToLowerInvariant().Split(Sep);
            var right = y.ToString().ToLowerInvariant().Split(Sep);
            for (var i = 0; i < left.Length && i < right.Length; i++)
            {
                var order = CompareCodePoints(left[i], right[i]);
                if (order != 0)
                {
                    return order;
                }
            }

            return left.Length.CompareTo(right.Length);
        }

        /// <summary>
        /// Python's <c>str</c> order: by code point. UTF-16 ordinal order differs only where a surrogate pair (a code
        /// point above U+FFFF) meets a unit from U+E000 to U+FFFF, so those are shifted into code point order.
        /// </summary>
        private static int CompareCodePoints(string left, string right)
        {
            for (var i = 0; i < left.Length && i < right.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return Fixup(left[i]).CompareTo(Fixup(right[i]));
                }
            }

            return left.Length.CompareTo(right.Length);

            static int Fixup(char c) => c >= 0xE000 ? c - 0x800 : c >= 0xD800 ? c + 0x2000 : c;
        }
    }
}
