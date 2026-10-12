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
}
