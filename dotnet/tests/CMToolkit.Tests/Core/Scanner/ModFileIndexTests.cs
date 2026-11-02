using CMToolkit.App.Startup;
using CMToolkit.Core.Overview;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The mod file index, built directly over the <c>scanner/data-walk-mo2</c> scenario's MO2 staging folder. Its
/// <c>modlist.txt</c>, read bottom-up, enables LowMod then HighMod, and the overwrite folder comes last; its INI skips
/// the folders <c>Extra</c> and <c>Junk Dir</c>, and the default skip suffix is <c>.mohidden</c>.
/// </summary>
public sealed class ModFileIndexTests
{
    private const string Mods = @"LocalAppData\ModOrganizer\Fallout 4\mods";
    private const string Overwrite = @"LocalAppData\ModOrganizer\Fallout 4\overwrite";

    [Fact]
    public async Task The_highest_priority_mod_wins_a_path_whatever_its_case()
    {
        using var machine = Scenario();

        var index = await Build(machine);

        // LowMod has Textures\shared.png and HighMod has Textures\Shared.png; HighMod is above LowMod in modlist.txt.
        var file = index.Files[new PythonPath(@"Textures\SHARED.PNG")];
        Assert.Equal("HighMod", file.Mod);
        Assert.Equal(machine.PathOf($@"{Mods}\HighMod\Textures\Shared.png"), file.Path.ToString());
        var folder = index.Folders[new PythonPath("textures")];
        Assert.Equal("HighMod", folder.Mod);
        Assert.Equal(machine.PathOf($@"{Mods}\HighMod\Textures"), folder.Path.ToString());

        // A path only the lower mod has stays the lower mod's.
        Assert.Equal("LowMod", index.Files[new PythonPath(@"Textures\low.dds")].Mod);
    }

    [Fact]
    public async Task Skip_folders_are_pruned_whatever_their_case()
    {
        using var machine = Scenario();

        var index = await Build(machine);

        // The INI says "Extra, Junk Dir"; LowMod's folders are Extra and "junk dir".
        Assert.DoesNotContain(index.Folders.Keys, k => k.ToString() is "Extra" or "junk dir");
        Assert.DoesNotContain(index.Files.Keys, k => k.ToString() is @"Extra\x.txt" or @"junk dir\y.txt");
    }

    [Fact]
    public async Task Files_with_a_skip_suffix_are_left_out()
    {
        using var machine = Scenario();

        var index = await Build(machine);

        Assert.False(index.Files.ContainsKey(new PythonPath(@"Textures\hidden.dds.mohidden")));
        // A name that only ends like a suffix's last character is kept.
        Assert.True(index.Files.ContainsKey(new PythonPath(@"Textures\tex.dds (2)")));
    }

    [Fact]
    public async Task A_mods_root_modules_and_archives_are_indexed_by_file_name_and_the_root_has_no_folder_entry()
    {
        using var machine = Scenario();

        var index = await Build(machine);

        Assert.Equal(["BadMagic.esp", "HighMod.esp"], index.Modules.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("LowMod", index.Modules["BadMagic.esp"].Mod);
        Assert.Equal(machine.PathOf($@"{Mods}\LowMod\BadMagic.esp"), index.Modules["BadMagic.esp"].Path.ToString());
        Assert.Equal("HighMod", index.Modules["HighMod.esp"].Mod);
        var archive = Assert.Single(index.Archives);
        Assert.Equal("BadMagic - Main.ba2", archive.Key);
        Assert.Equal("HighMod", archive.Value.Mod);
        Assert.Equal(machine.PathOf($@"{Mods}\HighMod\BadMagic - Main.ba2"), archive.Value.Path.ToString());

        // The file-name keys are plain strings, so they match case-sensitively.
        Assert.False(index.Modules.ContainsKey("highmod.esp"));
        Assert.False(index.Folders.ContainsKey(new PythonPath(".")));
    }

    [Fact]
    public async Task The_overwrite_folder_outranks_every_mod()
    {
        using var machine = Scenario();
        // The same file as the highest-priority mod's, in the overwrite folder.
        Directory.CreateDirectory(machine.PathOf($@"{Overwrite}\Textures"));
        File.WriteAllText(machine.PathOf($@"{Overwrite}\Textures\shared.png"), "");

        var index = await Build(machine);

        var file = index.Files[new PythonPath(@"Textures\shared.png")];
        Assert.Equal("overwrite", file.Mod);
        Assert.Equal(machine.PathOf($@"{Overwrite}\Textures\shared.png"), file.Path.ToString());
        Assert.Equal("overwrite", index.Folders[new PythonPath("Textures")].Mod);
    }

    [Fact]
    public async Task A_file_belongs_to_the_mod_that_has_it_at_its_staging_path()
    {
        using var machine = Scenario();
        var index = await Build(machine);

        var (mod, path) = index.OwnerOf(
            new PythonPath(@"Textures\shared.png"), ModPathKind.File, new PythonPath(machine.PathOf(@"Game\Data\Textures\shared.png")));

        Assert.Equal("HighMod", mod);
        Assert.Equal(machine.PathOf($@"{Mods}\HighMod\Textures\Shared.png"), path.ToString());
    }

    [Fact]
    public async Task A_folder_belongs_to_the_mod_that_has_it_at_its_staging_path()
    {
        using var machine = Scenario();
        var index = await Build(machine);

        var (mod, path) = index.OwnerOf(
            new PythonPath(@"Meshes\AnimTextData"), ModPathKind.Folder, new PythonPath(machine.PathOf(@"Game\Data\Meshes\AnimTextData")));

        Assert.Equal("LowMod", mod);
        Assert.Equal(machine.PathOf($@"{Mods}\LowMod\Meshes\AnimTextData"), path.ToString());
    }

    [Fact]
    public async Task A_path_no_mod_has_belongs_to_no_mod_at_its_data_path()
    {
        using var machine = Scenario();
        var index = await Build(machine);
        var data = machine.PathOf(@"Game\Data\Textures\Unmanaged.png");

        var (mod, path) = index.OwnerOf(new PythonPath(@"Textures\Unmanaged.png"), ModPathKind.File, new PythonPath(data));

        Assert.Equal("", mod);
        Assert.Equal(data, path.ToString());
    }

    [Fact]
    public async Task A_folder_lookup_does_not_find_a_file_of_the_same_path()
    {
        using var machine = Scenario();
        var index = await Build(machine);
        var data = machine.PathOf(@"Game\Data\Textures\low.dds");

        var (mod, path) = index.OwnerOf(new PythonPath(@"Textures\low.dds"), ModPathKind.Folder, new PythonPath(data));

        Assert.Equal("", mod);
        Assert.Equal(data, path.ToString());
    }

    private static MaterializedScenario Scenario()
        => ParityScenarios.All().Single(s => s.Id == "scanner/data-walk-mo2").Materialize();

    /// <summary>
    /// Locates the scenario's game, gathers its Overview as the scan's refresh would, snapshots every box ticked on the
    /// scenario's own machine, and builds the index from that snapshot.
    /// </summary>
    private static async Task<ModFiles> Build(MaterializedScenario machine)
    {
        var game = await Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
            ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());
        var settings = ScanSettings.Snapshot(ScanSettings.All.ToDictionary(s => s, _ => true), game, report, machine.Host);

        var index = ModFileIndex.Build(settings, out var failure);

        Assert.Null(failure);
        return index ?? throw new InvalidOperationException("The scenario isn't in MO2 stage mode.");
    }
}
