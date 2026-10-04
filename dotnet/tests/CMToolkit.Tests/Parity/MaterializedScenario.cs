using CMToolkit.Core;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// A Parity Scenario copied to a temp root: the fake machine a test runs Core against. Disposing it deletes the root.
/// </summary>
/// <remarks>
/// The root must be on NTFS, because goldens compare directory enumeration order exactly and that order (FindFirstFile)
/// is a property of the file system (ADR-0004). Set <c>CMT_PARITY_TEMP</c> to put roots somewhere other than the
/// system temp folder, for example when that is on a ReFS Dev Drive.
/// </remarks>
public sealed class MaterializedScenario : IDisposable
{
    /// <summary>The token that stands for <see cref="Root"/> in host blocks and goldens.</summary>
    public const string RootToken = "<ROOT>";

    /// <summary>The environment variable that overrides the folder temp roots are created in.</summary>
    public const string TempOverrideVariable = "CMT_PARITY_TEMP";

    private static readonly Dictionary<string, FileAttributes> AttributeVocabulary = new(StringComparer.Ordinal)
    {
        ["readonly"] = FileAttributes.ReadOnly,
        ["hidden"] = FileAttributes.Hidden,
        ["system"] = FileAttributes.System,
    };

    private MaterializedScenario(ParityScenario scenario, string root)
    {
        Scenario = scenario;
        Root = root;
        Host = new FakeHostEnvironment(scenario.Manifest.Host, root);
    }

    /// <summary>The scenario this machine was built from.</summary>
    public ParityScenario Scenario { get; }

    /// <summary>The temp root's absolute path, without a trailing separator.</summary>
    public string Root { get; }

    /// <summary>The fake host, driven by the scenario's <c>host</c> block.</summary>
    public FakeHostEnvironment Host { get; }

    /// <summary>Whether the scenario has an <c>http/</c> folder of canned responses.</summary>
    public bool HasHttp => Directory.Exists(HttpDirectory);

    private string HttpDirectory => Path.Combine(Scenario.Directory, "http");

    /// <summary>
    /// A fresh handler serving the scenario's <c>http/</c> folder. Each call returns a new one, because an
    /// <see cref="HttpClient"/> disposes its handler.
    /// </summary>
    public ParityHttpHandler CreateHttpHandler() => new(HttpDirectory);

    /// <summary>The absolute path of a tree-relative path such as <c>Game/Data</c>.</summary>
    public string PathOf(string treeRelative) => Path.GetFullPath(Path.Combine(Root, treeRelative));

    /// <summary>
    /// A path as goldens write it: <c>&lt;ROOT&gt;</c> plus the root-relative part with forward slashes, for example
    /// <c>&lt;ROOT&gt;/Game/Data</c>. The Python driver projects paths the same way.
    /// </summary>
    /// <exception cref="ArgumentException">The path is outside the root, which would leak a machine-specific path into a golden.</exception>
    public string ToGoldenPath(string path)
    {
        var relative = Path.GetRelativePath(Root, Path.GetFullPath(path));
        if (relative == ".")
        {
            return RootToken;
        }

        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new ArgumentException($"'{path}' is outside the scenario root '{Root}'.", nameof(path));
        }

        return $"{RootToken}/{relative.Replace('\\', '/')}";
    }

    /// <summary>Builds the machine for <paramref name="scenario"/>. See <see cref="ParityScenario.Materialize"/>.</summary>
    internal static MaterializedScenario Create(ParityScenario scenario, string? tempBase)
    {
        var manifest = scenario.Manifest;
        var root = Path.Combine(TempBase(tempBase), "cmt-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var machine = new MaterializedScenario(scenario, root);
        try
        {
            var tree = Path.Combine(scenario.Directory, "tree");
            if (Directory.Exists(tree))
            {
                CopyTree(tree, root);
            }

            foreach (var dir in manifest.EmptyDirs ?? [])
            {
                var full = machine.PathOf(dir);
                if (File.Exists(full))
                {
                    throw new InvalidDataException($"{scenario.Id}: emptyDirs entry '{dir}' is a file in the tree.");
                }

                Directory.CreateDirectory(full);
            }

            foreach (var entry in manifest.Attributes ?? [])
            {
                ApplyAttributes(scenario, machine.PathOf(entry.Path), entry);
            }

            return machine;
        }
        catch
        {
            machine.Dispose();
            throw;
        }
    }

    private static string TempBase(string? requested)
    {
        var path = Path.GetFullPath(requested
                                    ?? (Environment.GetEnvironmentVariable(TempOverrideVariable) is { Length: > 0 } custom
                                        ? custom
                                        : Path.GetTempPath()));
        var format = new DriveInfo(Path.GetPathRoot(path)!).DriveFormat;
        if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Parity Scenario roots must be on NTFS, but {path} is on {format}. Set {TempOverrideVariable} to an NTFS folder.");
        }

        return path;
    }

    /// <summary>Copies files and folders, including empty ones and hidden ones. Attributes come only from the manifest.</summary>
    private static void CopyTree(string source, string destination)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            File.Copy(file, target);
            // A checkout can leave its own attributes (read-only, for example); the manifest is the only source.
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    private static void ApplyAttributes(ParityScenario scenario, string path, AttributeEntry entry)
    {
        var isDirectory = Directory.Exists(path);
        if (!isDirectory && !File.Exists(path))
        {
            throw new InvalidDataException($"{scenario.Id}: attributes entry '{entry.Path}' doesn't exist in the tree.");
        }

        var attributes = File.GetAttributes(path) & ~FileAttributes.Normal;
        foreach (var name in entry.Set)
        {
            attributes |= AttributeVocabulary.TryGetValue(name, out var flag)
                ? flag
                : throw new InvalidDataException(
                    $"{scenario.Id}: unknown attribute '{name}'; the vocabulary is {string.Join(", ", AttributeVocabulary.Keys)}.");
        }

        File.SetAttributes(path, attributes);
    }

    /// <summary>
    /// Deletes the root. Read-only, hidden and system attributes are cleared first, since <see cref="Directory.Delete(string, bool)"/>
    /// refuses read-only files.
    /// </summary>
    public void Dispose()
    {
        try
        {
            var root = new DirectoryInfo(Root);
            if (!root.Exists)
            {
                return;
            }

            foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                entry.Attributes = entry is DirectoryInfo ? FileAttributes.Directory : FileAttributes.Normal;
            }

            root.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp root (a file a test left open, say) must not fail the test that used it.
        }
    }
}

/// <summary>
/// The fake <see cref="IHostEnvironment"/> for parity tests, driven by a scenario's <c>host</c> block and rooted at its
/// temp root.
/// </summary>
public sealed class FakeHostEnvironment : IHostEnvironment
{
    /// <summary>A host for <paramref name="block"/>, with <c>&lt;ROOT&gt;</c> standing for <paramref name="root"/>.</summary>
    public FakeHostEnvironment(HostBlock block, string root)
    {
        Block = block;
        Root = root;
    }

    /// <summary>The whole host block, for members later slices add.</summary>
    public HostBlock Block { get; }

    /// <summary>The scenario's temp root.</summary>
    public string Root { get; }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The host block has no <c>appDir</c>.</exception>
    public string AppDirectory => Expand(Block.AppDir ?? throw new InvalidOperationException("The scenario's host block has no appDir."));

    /// <summary>
    /// Expands a leading <c>&lt;ROOT&gt;</c> to <see cref="Root"/>. The rest of the string is kept as written, so a
    /// value can use whichever separator the API it fakes would return.
    /// </summary>
    public string Expand(string value)
        => value.StartsWith(MaterializedScenario.RootToken, StringComparison.Ordinal)
            ? Root + value[MaterializedScenario.RootToken.Length..]
            : value;
}
