using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core.ArchivePatcher;

/// <summary>
/// The parts of the <c>archive-patcher</c> operation's golden that the Core and App replays share: the script's fixed
/// filter, the <c>Changes/</c> overlay (<c>apply_changes</c>), the Data snapshot after a run (<c>data_snapshot</c>) and
/// path tokens as <c>GoldenContext.path</c> writes them.
/// </summary>
internal static class ArchivePatcherGolden
{
    /// <summary><c>ARCHIVE_PATCHER_FILTER</c>: a capital sharp s, which case-folds to <c>ss</c>.</summary>
    public const string Filter = "ẞ";

    /// <summary>The script's steps, in order, as the golden names them.</summary>
    public static readonly string[] Steps = ["opened", "patched", "v8", "filtered", "patchedFiltered", "unfiltered"];

    /// <summary>
    /// <c>apply_changes</c>: copies each file under the tree's <c>Changes/</c> over the same root-relative path, and for
    /// each <c>&lt;name&gt;.delete</c> deletes <c>&lt;name&gt;</c>, in the driver's order (sorted paths). Returns the
    /// changed paths as golden tokens.
    /// </summary>
    public static JsonArray ApplyChanges(MaterializedScenario machine)
    {
        var changes = Path.Combine(machine.Root, "Changes");
        var applied = new JsonArray();
        if (!Directory.Exists(changes))
        {
            return applied;
        }

        // Python sorts the Path objects: by their parts, which for these flat names is an ordinal sort.
        foreach (var source in Directory.EnumerateFiles(changes, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(changes, source);
            string target;
            if (Path.GetExtension(relative) == ".delete")
            {
                target = Path.Combine(machine.Root, relative[..^".delete".Length]);
                File.Delete(target);
            }
            else
            {
                target = Path.Combine(machine.Root, relative);
                File.WriteAllBytes(target, File.ReadAllBytes(source));
            }

            applied.Add(GoldenPath(machine, target));
        }

        return applied;
    }

    /// <summary>
    /// <c>data_snapshot</c>: every file under <paramref name="data"/> with its golden path, size and SHA-256, sorted by
    /// the case-folded full path.
    /// </summary>
    public static JsonArray DataSnapshot(MaterializedScenario machine, string data) => new([..
        Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories)
            .OrderBy(PythonCaseFold.Of, StringComparer.Ordinal)
            .Select(path => (JsonNode)new JsonObject
            {
                ["path"] = GoldenPath(machine, path),
                ["size"] = new FileInfo(path).Length,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            })]);

    /// <summary>
    /// <c>GoldenContext.path</c>: <c>&lt;ROOT&gt;</c> plus the root-relative part with forward slashes, kept as written
    /// (a <c>..</c> stays, as <c>PureWindowsPath.relative_to</c> keeps it).
    /// </summary>
    public static string GoldenPath(MaterializedScenario machine, string path)
    {
        if (!path.StartsWith(machine.Root, StringComparison.OrdinalIgnoreCase) || path.Length <= machine.Root.Length || path[machine.Root.Length] != '\\')
        {
            throw new ArgumentException($"'{path}' is outside the scenario root '{machine.Root}'.", nameof(path));
        }

        return MaterializedScenario.RootToken + path[machine.Root.Length..].Replace('\\', '/');
    }

    /// <summary>A set of paths as the golden's unordered array.</summary>
    public static JsonObject Unordered(MaterializedScenario machine, IEnumerable<PythonPath> paths)
        => new() { [GoldenComparer.UnorderedKey] = new JsonArray([.. paths.Select(p => (JsonNode)GoldenPath(machine, p.ToString()))]) };
}
