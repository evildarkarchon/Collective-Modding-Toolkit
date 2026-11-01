using System.Text.Json.Nodes;
using CMToolkit.Tests.Support;
using Xunit.Sdk;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// One recorded Parity Scenario under <c>parity/scenarios/&lt;module&gt;/&lt;scenario&gt;/</c>: its <c>tree/</c>, its
/// <c>scenario.json</c>, its optional <c>http/</c> folder, and its one <c>golden.json</c>: the result a replay must
/// produce.
/// </summary>
/// <remarks>
/// It is <see cref="IXunitSerializable"/> so that each scenario shows up as its own row in test explorers and results. Only the scenarios root
/// and the ID are serialized; the manifest and golden are read from disk when first used.
/// </remarks>
public sealed class ParityScenario : IXunitSerializable
{
    private ScenarioManifest? _manifest;

    /// <summary>For xUnit's deserializer only.</summary>
    public ParityScenario()
    {
        ScenariosRoot = string.Empty;
        Id = string.Empty;
    }

    /// <summary>A scenario in <paramref name="scenariosRoot"/>, by its <c>&lt;module&gt;/&lt;scenario&gt;</c> ID.</summary>
    public ParityScenario(string scenariosRoot, string id)
    {
        ScenariosRoot = scenariosRoot;
        Id = id;
    }

    /// <summary>The folder holding the module folders, normally <see cref="ParityPaths.Scenarios"/>.</summary>
    public string ScenariosRoot { get; private set; }

    /// <summary><c>&lt;module&gt;/&lt;scenario&gt;</c>, with a forward slash, for example <c>settings/download-source-github</c>.</summary>
    public string Id { get; private set; }

    /// <summary>The module folder name, the part of <see cref="Id"/> before the slash.</summary>
    public string Module => Id[..Id.IndexOf('/', StringComparison.Ordinal)];

    /// <summary>The scenario's folder.</summary>
    public string Directory => Path.Combine(ScenariosRoot, Id.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The parsed <c>scenario.json</c>.</summary>
    public ScenarioManifest Manifest => _manifest ??= ScenarioManifest.Parse(File.ReadAllText(Path.Combine(Directory, "scenario.json")));

    /// <summary>The golden's file name. A scenario has exactly one golden (ADR-0008).</summary>
    public const string GoldenFileName = "golden.json";

    /// <summary>
    /// The app version a replay runs as when its result depends on the running version, such as which canned release
    /// is an update. Every golden is recorded at this version, whatever version the app is built as, so a release
    /// changes no golden.
    /// </summary>
    public const string RecordedAppVersion = "0.6.2-dev";

    /// <summary>
    /// The file a failing replay writes its result to, beside the golden (ADR-0008). Git ignores it, so only a golden
    /// someone accepted is committed.
    /// </summary>
    public const string NewGoldenFileName = "golden.new.json";

    /// <summary>
    /// Serializes reads and writes of goldens within the test run. Replays of one scenario run in parallel, and while
    /// <see cref="GoldenAssert"/> records, a reader must never see a half-written <c>golden.json</c>.
    /// </summary>
    internal static readonly Lock GoldenFileLock = new();

    /// <summary>Whether the scenario has a golden.</summary>
    public bool IsRecorded => File.Exists(GoldenPath);

    /// <summary>The scenario's <c>golden.json</c>.</summary>
    internal string GoldenPath => Path.Combine(Directory, GoldenFileName);

    /// <summary>The scenario's <c>golden.new.json</c>, whether or not it exists.</summary>
    internal string NewGoldenPath => Path.Combine(Directory, NewGoldenFileName);

    /// <summary>
    /// Reads what the C# replay must produce: the scenario's <c>golden.json</c>. The manifest's <c>deviations</c> entries
    /// are a record of what the port fixed and play no part in which file is read.
    /// </summary>
    /// <exception cref="InvalidOperationException">The scenario hasn't been recorded.</exception>
    public JsonNode? LoadGolden()
    {
        if (!IsRecorded)
        {
            throw new InvalidOperationException(
                $"Parity Scenario '{Id}' has no golden.json. Record one by running a test that replays it with "
                + $"{GoldenAssert.RecordVariable}=1, or by accepting the {NewGoldenFileName} a failing replay writes.");
        }

        lock (GoldenFileLock)
        {
            return JsonNode.Parse(File.ReadAllText(GoldenPath));
        }
    }

    /// <summary>Copies the tree to a fresh NTFS temp root and applies the manifest's empty folders and attributes.</summary>
    /// <param name="tempBase">
    /// The folder to create the root in. Defaults to <c>CMT_PARITY_TEMP</c> or the system temp folder; the harness's
    /// own tests pass a private folder so they can check what is left behind without racing parallel tests.
    /// </param>
    /// <returns>The fake machine. Dispose it to delete the temp root.</returns>
    public MaterializedScenario Materialize(string? tempBase = null) => MaterializedScenario.Create(this, tempBase);

    /// <inheritdoc/>
    public void Deserialize(IXunitSerializationInfo info)
    {
        ScenariosRoot = (string)info.GetValue(nameof(ScenariosRoot))!;
        Id = (string)info.GetValue(nameof(Id))!;
    }

    /// <inheritdoc/>
    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(ScenariosRoot), ScenariosRoot);
        info.AddValue(nameof(Id), Id);
    }

    /// <summary>The ID, which is what test explorers show for the theory row.</summary>
    public override string ToString() => Id;
}

/// <summary>The <c>TheoryData</c> source for Parity Scenario tests.</summary>
public static class ParityScenarios
{
    /// <summary>Every scenario under <paramref name="scenariosRoot"/> (default <see cref="ParityPaths.Scenarios"/>), by ID.</summary>
    public static IReadOnlyList<ParityScenario> All(string? scenariosRoot = null)
    {
        var root = scenariosRoot ?? ParityPaths.Scenarios;
        if (!System.IO.Directory.Exists(root))
        {
            return [];
        }

        return System.IO.Directory.EnumerateDirectories(root)
            .SelectMany(module => System.IO.Directory.EnumerateDirectories(module)
                .Where(dir => File.Exists(Path.Combine(dir, "scenario.json")))
                .Select(dir => new ParityScenario(root, $"{Path.GetFileName(module)}/{Path.GetFileName(dir)}")))
            .OrderBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The scenarios of one module that run one operation, as theory data. Use it through <c>[MemberData]</c>; the
    /// guard test finds every such member to check that no scenario on disk goes unexercised.
    /// </summary>
    /// <exception cref="InvalidOperationException">No scenario matches, which would otherwise be a silently empty theory.</exception>
    public static TheoryData<ParityScenario> Of(string module, string operation, string? scenariosRoot = null)
    {
        var matches = All(scenariosRoot)
            .Where(s => s.Module == module && s.Manifest.Operation == operation)
            .ToList();
        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"No Parity Scenario in module '{module}' runs operation '{operation}'.");
        }

        return new TheoryData<ParityScenario>(matches);
    }
}

/// <summary>Where the parity harness lives in the repo.</summary>
public static class ParityPaths
{
    /// <summary>The repo root, the folder above <c>dotnet/</c>.</summary>
    public static string RepoRoot => Path.GetDirectoryName(RepoPaths.DotnetRoot)!;

    /// <summary><c>parity/</c>.</summary>
    public static string Parity => Path.Combine(RepoRoot, "parity");

    /// <summary><c>parity/scenarios/</c>.</summary>
    public static string Scenarios => Path.Combine(Parity, "scenarios");
}
