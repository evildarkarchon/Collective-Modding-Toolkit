using System.Text;
using System.Text.Json;
using CMToolkit.Tests.Support;
using Xunit.Sdk;

namespace CMToolkit.Tests.Parity;

/// <summary>
/// The scenario source and materializer, run against throwaway scenarios in a temp folder rather than the committed
/// ones under <c>parity/scenarios/</c>.
/// </summary>
public sealed class ParityScenarioTests : IDisposable
{
    private readonly TempDirectory _scenarios = new();

    public void Dispose() => _scenarios.Dispose();

    private ParityScenario Author(string id, string manifestJson, params (string Path, byte[] Bytes)[] treeFiles)
    {
        _scenarios.WriteBytes(Path.Combine(id, "scenario.json"), Encoding.UTF8.GetBytes(manifestJson));
        foreach (var (path, bytes) in treeFiles)
        {
            _scenarios.WriteBytes(Path.Combine(id, "tree", path), bytes);
        }

        return new ParityScenario(_scenarios.Path, id);
    }

    private const string MinimalManifest = """{"operation": "op", "parity": ["SET-2"], "host": {"appDir": "<ROOT>/App"}}""";

    [Fact]
    public void Materializing_copies_the_tree_byte_for_byte_into_a_fresh_ntfs_root()
    {
        var scenario = Author("m/s", MinimalManifest, ("App/assets/download-source.txt", [0xEF, 0xBB, 0xBF, (byte)'x', (byte)'\r', (byte)'\n']));

        using var machine = scenario.Materialize();

        Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(machine.Root)!).DriveFormat);
        Assert.StartsWith("cmt-parity-", Path.GetFileName(machine.Root));
        Assert.Equal(
            [0xEF, 0xBB, 0xBF, (byte)'x', (byte)'\r', (byte)'\n'],
            File.ReadAllBytes(machine.PathOf("App/assets/download-source.txt")));
    }

    [Fact]
    public void Each_materialization_gets_its_own_root_and_dispose_deletes_it()
    {
        var scenario = Author("m/s", MinimalManifest, ("a.txt", [1]));

        var first = scenario.Materialize();
        using var second = scenario.Materialize();
        first.Dispose();

        Assert.NotEqual(first.Root, second.Root);
        Assert.False(Directory.Exists(first.Root));
        Assert.True(File.Exists(second.PathOf("a.txt")));
    }

    [Fact]
    public void Empty_dirs_are_created_and_a_scenario_without_a_tree_is_allowed()
    {
        var scenario = Author("m/s", """
            {"operation": "op", "parity": [], "host": {}, "emptyDirs": ["App/assets/download-source.txt", "Game/Data"]}
            """);

        using var machine = scenario.Materialize();

        Assert.True(Directory.Exists(machine.PathOf("App/assets/download-source.txt")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(machine.PathOf("Game/Data")));
    }

    [Fact]
    public void Attributes_are_applied_from_the_closed_vocabulary_and_dispose_still_deletes_read_only_files()
    {
        var scenario = Author(
            "m/s",
            """{"operation": "op", "parity": [], "host": {}, "attributes": [{"path": "f.txt", "set": ["readonly", "hidden"]}, {"path": "d", "set": ["system"]}]}""",
            ("f.txt", [1]),
            ("d/g.txt", [2]));

        var machine = scenario.Materialize();
        var fileAttributes = File.GetAttributes(machine.PathOf("f.txt"));
        var dirAttributes = File.GetAttributes(machine.PathOf("d"));
        machine.Dispose();

        Assert.True(fileAttributes.HasFlag(FileAttributes.ReadOnly));
        Assert.True(fileAttributes.HasFlag(FileAttributes.Hidden));
        Assert.True(dirAttributes.HasFlag(FileAttributes.System));
        Assert.True(dirAttributes.HasFlag(FileAttributes.Directory));
        Assert.False(Directory.Exists(machine.Root));
    }

    [Fact]
    public void Files_in_the_tree_start_with_no_attributes_of_their_own()
    {
        var scenario = Author("m/s", MinimalManifest, ("f.txt", [1]));
        File.SetAttributes(Path.Combine(scenario.Directory, "tree", "f.txt"), FileAttributes.ReadOnly);
        try
        {
            using var machine = scenario.Materialize();

            Assert.False(File.GetAttributes(machine.PathOf("f.txt")).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            File.SetAttributes(Path.Combine(scenario.Directory, "tree", "f.txt"), FileAttributes.Normal);
        }
    }

    [Fact]
    public void An_attribute_outside_the_vocabulary_is_rejected_and_the_root_is_cleaned_up()
    {
        var scenario = Author(
            "m/s",
            """{"operation": "op", "parity": [], "host": {}, "attributes": [{"path": "f.txt", "set": ["archive"]}]}""",
            ("f.txt", [1]));
        using var tempBase = new TempDirectory();

        var ex = Assert.Throws<InvalidDataException>(() => scenario.Materialize(tempBase.Path));

        Assert.Contains("unknown attribute 'archive'", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tempBase.Path));
    }

    [Fact]
    public void An_attribute_on_a_missing_path_is_rejected()
    {
        var scenario = Author("m/s", """{"operation": "op", "parity": [], "host": {}, "attributes": [{"path": "nope", "set": ["hidden"]}]}""");

        Assert.Throws<InvalidDataException>(() => scenario.Materialize());
    }

    [Theory]
    [InlineData("emptyDirs", "../escaped")]
    [InlineData("emptyDirs", "<ABS>/escaped")]
    [InlineData("emptyDirs", "")]
    [InlineData("attributes", "..")]
    [InlineData("attributes", "<ABS>")]
    public void Manifest_paths_that_dont_resolve_beneath_the_root_are_rejected_without_touching_anything(string field, string entry)
    {
        // Every escape lands in tempBase (the root's parent), so a regression would show up as a leftover there rather
        // than as a change somewhere else on the machine.
        using var tempBase = new TempDirectory();
        entry = entry.Replace("<ABS>", tempBase.Path.Replace('\\', '/'), StringComparison.Ordinal);
        var extra = field == "emptyDirs"
            ? $"\"emptyDirs\": [{JsonSerializer.Serialize(entry)}]"
            : $"\"attributes\": [{{\"path\": {JsonSerializer.Serialize(entry)}, \"set\": [\"hidden\"]}}]";
        var scenario = Author("m/s", $$"""{"operation": "op", "parity": [], "host": {}, {{extra}}}""");

        var ex = Assert.Throws<InvalidDataException>(() => scenario.Materialize(tempBase.Path));

        Assert.Contains("doesn't resolve beneath the tree root", ex.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tempBase.Path));
        Assert.False(File.GetAttributes(tempBase.Path).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void A_name_that_merely_starts_with_two_dots_stays_inside_the_root()
    {
        var scenario = Author("m/s", """{"operation": "op", "parity": [], "host": {}, "emptyDirs": ["..data"]}""");

        using var machine = scenario.Materialize();

        Assert.True(Directory.Exists(machine.PathOf("..data")));
        Assert.Equal($"{MaterializedScenario.RootToken}/..data", machine.ToGoldenPath(machine.PathOf("..data")));
    }

    [Fact]
    public void Unknown_manifest_keys_are_rejected_so_typos_fail_loudly()
    {
        var scenario = Author("m/s", """{"operation": "op", "parity": [], "host": {"appdir": "<ROOT>/App"}}""");

        Assert.Throws<JsonException>(() => scenario.Manifest);
    }

    [Fact]
    public void Required_manifest_keys_must_be_present()
    {
        var scenario = Author("m/s", """{"operation": "op", "host": {}}""");

        Assert.Throws<JsonException>(() => scenario.Manifest);
    }

    [Fact]
    public void The_full_host_block_schema_parses()
    {
        var scenario = Author("m/s", """
            {
              "operation": "op",
              "parity": ["MM-1"],
              "host": {
                "appDir": "<ROOT>/App",
                "cwd": "<ROOT>/Cwd",
                "argv0": "cm-toolkit.exe",
                "env": {"LOCALAPPDATA": "<ROOT>/LocalAppData"},
                "knownFolders": {"Documents": "<ROOT>/Documents", "LocalAppData": "<ROOT>/LocalAppData"},
                "registry": {"HKLM\\SOFTWARE\\WOW6432Node\\Bethesda Softworks\\Fallout4": {"Installed Path": {"type": "REG_SZ", "value": "<ROOT>\\Game"}}},
                "processes": [{"name": "ModOrganizer.exe", "exe": "<ROOT>/MO2/ModOrganizer.exe", "fileVersion": [2, 5, 2, 0]}, {"name": "explorer.exe"}],
                "os": {"system": "Windows", "release": "11", "build": 26100},
                "pc": {"ramBytes": 68719476736},
                "dialogs": [{"function": "askyesno", "answer": true}]
              }
            }
            """);

        var host = scenario.Manifest.Host;

        Assert.Equal("cm-toolkit.exe", host.Argv0);
        Assert.Equal([2, 5, 2, 0], host.Processes![0].FileVersion!);
        Assert.Null(host.Processes[1].Exe);
        Assert.False(host.Os!.Wine);
        Assert.Equal("<ROOT>\\Game", host.Registry!.Single().Value["Installed Path"].Value.GetString());
        Assert.True(host.Dialogs![0].Answer.GetBoolean());
    }

    [Fact]
    public void The_fake_host_expands_the_root_token_and_keeps_the_rest_as_written()
    {
        var scenario = Author("m/s", MinimalManifest);
        using var machine = scenario.Materialize();

        Assert.Equal(machine.Root + "/App", machine.Host.AppDirectory);
        Assert.Equal(machine.Root + @"\Game", machine.Host.Expand(@"<ROOT>\Game"));
        Assert.Equal(@"C:\Windows\explorer.exe", machine.Host.Expand(@"C:\Windows\explorer.exe"));
    }

    [Fact]
    public void A_host_without_an_app_dir_says_so()
    {
        using var machine = Author("m/s", """{"operation": "op", "parity": [], "host": {}}""").Materialize();

        Assert.Throws<InvalidOperationException>(() => machine.Host.AppDirectory);
    }

    [Fact]
    public void Golden_paths_are_root_relative_tokens_and_paths_outside_the_root_are_refused()
    {
        using var machine = Author("m/s", MinimalManifest).Materialize();

        Assert.Equal("<ROOT>", machine.ToGoldenPath(machine.Root));
        Assert.Equal("<ROOT>/Game/Data/a.esp", machine.ToGoldenPath(Path.Combine(machine.Root, "Game", "Data", "a.esp")));
        Assert.Throws<ArgumentException>(() => machine.ToGoldenPath(Path.GetTempPath()));
    }

    [Fact]
    public void An_unrecorded_scenario_points_at_the_recording_command()
    {
        var scenario = Author("m/s", MinimalManifest);

        Assert.False(scenario.IsRecorded);
        var ex = Assert.Throws<InvalidOperationException>(() => scenario.LoadGolden());
        Assert.Contains("parity/driver/record.py m/s", ex.Message);
    }

    [Fact]
    public void The_source_lists_scenarios_by_id_and_filters_by_module_and_operation()
    {
        Author("settings/b", """{"operation": "x", "parity": [], "host": {}}""");
        Author("settings/a", """{"operation": "x", "parity": [], "host": {}}""");
        Author("settings/c", """{"operation": "y", "parity": [], "host": {}}""");
        Author("overview/a", """{"operation": "x", "parity": [], "host": {}}""");
        _scenarios.WriteBytes(Path.Combine("settings", "not-a-scenario", "notes.txt"), [1]);

        Assert.Equal(["overview/a", "settings/a", "settings/b", "settings/c"], ParityScenarios.All(_scenarios.Path).Select(s => s.Id));
        Assert.Equal(
            ["settings/a", "settings/b"],
            ParityScenarios.Of("settings", "x", _scenarios.Path).Select(row => row.Data.Id));
        Assert.Throws<InvalidOperationException>(() => ParityScenarios.Of("settings", "z", _scenarios.Path));
    }

    [Fact]
    public void A_scenario_round_trips_through_xunit_serialization()
    {
        var scenario = Author("settings/a", MinimalManifest);
        var info = new DictionarySerializationInfo();

        scenario.Serialize(info);
        var copy = new ParityScenario();
        copy.Deserialize(info);

        Assert.Equal(scenario.Id, copy.Id);
        Assert.Equal(scenario.Directory, copy.Directory);
        Assert.Equal("settings", copy.Module);
        Assert.Equal("settings/a", copy.ToString());
    }

    private sealed class DictionarySerializationInfo : IXunitSerializationInfo
    {
        private readonly Dictionary<string, object?> _values = [];

        public void AddValue(string key, object? value, Type? valueType = null) => _values[key] = value;

        public object? GetValue(string key) => _values.GetValueOrDefault(key);
    }
}
