using CMToolkit.App.Startup;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.Overview;

/// <summary>
/// What the <c>overview-full</c> scenarios can't record: files another process holds open without sharing them, which
/// Python's <c>open</c> reports as <c>PermissionError</c> (OVW-P10, OVW-P15); a refresh that dies part-way, which keeps
/// the previous report's later parts; and the count colours on every limit's boundaries (OVW-C1). Also the port's fixes
/// for files that aren't UTF-8 (bug #86), which the reference failed on.
/// </summary>
public sealed class OverviewGatheringTests
{
    [Fact]
    [Trait("Parity", "OVW-P10")]
    [Trait("Parity", "OVW-P15")]
    public async Task A_module_or_archive_held_open_exclusively_is_unreadable_with_its_problem()
    {
        using var machine = Scenario("overview/modules-archives-mixed");
        var game = await Locate(machine);
        // FileShare.None: CreateFile fails with a sharing violation, which CPython maps to EACCES, a PermissionError.
        using var module = File.Open(machine.PathOf(@"Game\Data\Unofficial.esp"), FileMode.Open, FileAccess.Read, FileShare.None);
        using var archive = File.Open(machine.PathOf(@"Game\Data\Fallout4 - Interface.ba2"), FileMode.Open, FileAccess.Read, FileShare.None);

        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        var unreadableModule = Assert.Single(report.Problems, p => p.ItemText == "Unofficial.esp");
        Assert.Equal(ProblemTypes.InvalidModule, unreadableModule.Kind);
        Assert.Equal("Failed to read module due to permissions or the file is missing.", unreadableModule.Summary);
        Assert.Equal(ModSource.Overview, unreadableModule.ModSource);
        Assert.Contains(report.Modules.Unreadable, p => p.Name == "Unofficial.esp");

        var unreadableArchive = Assert.Single(report.Problems, p => p.ItemText == "Fallout4 - Interface.ba2");
        Assert.Equal(ProblemTypes.InvalidArchive, unreadableArchive.Kind);
        Assert.Equal("Failed to read archive due to permissions or the file is missing.", unreadableArchive.Summary);
        Assert.Null(unreadableArchive.Solution);
        Assert.Equal("Fallout4 - Interface.ba2", unreadableArchive.RelativePath);
        Assert.Contains(report.Archives.Unreadable, p => p.Name == "Fallout4 - Interface.ba2");
        Assert.DoesNotContain(report.Archives.NextGen, p => p.Name == "Fallout4 - Interface.ba2");
    }

    [Fact]
    [Trait("Parity", "OVW-A4"), Deviation(86, "Format bytes that aren't UTF-8 are shown with replacement characters in the Invalid Archive problem, and the refresh completes.")]
    public async Task A_refresh_after_an_archive_format_stops_being_utf8_reports_it_as_invalid()
    {
        using var machine = Scenario("overview/modules-archives-mixed");
        var game = await Locate(machine);
        var loaded = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        // The format bytes of an enabled archive stop being UTF-8. The reference's strict decode of them for the
        // problem text raised (T-1, bug #86); the port replaces each bad byte, as errors="replace" would.
        var archive = machine.PathOf(@"Game\Data\Fo76 - Main.ba2");
        var bytes = File.ReadAllBytes(archive);
        bytes.AsSpan(8, 4).Fill(0xFF);
        File.WriteAllBytes(archive, bytes);

        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger(), loaded);

        Assert.Equal(loaded.Modules.Enabled.Count, report.Modules.Enabled.Count);
        Assert.Contains(report.Archives.Unreadable, p => IsFo76(p.Name));
        var problem = Assert.Single(report.Problems, p => IsFo76(p.ItemText));
        Assert.Equal(ProblemTypes.InvalidArchive, problem.Kind);
        Assert.Equal("Archive format (����) is not valid for Fallout 4.", problem.Summary);
        // The archives step ran to the end: every enabled archive was checked again.
        Assert.Equal(loaded.Archives.Enabled.Count, report.Archives.Enabled.Count);
    }

    [Fact]
    [Trait("Parity", "OVW-M4"), Deviation(86, "A Fallout4.ccc or plugins.txt that isn't UTF-8 is reported as an Unexpected Format problem and its UTF-8 lines are still read, instead of failing the Overview.")]
    public async Task A_cc_list_or_plugins_txt_that_isnt_utf8_is_reported_and_its_utf8_lines_still_read()
    {
        using var machine = Scenario("overview/modules-archives-mixed");
        var game = await Locate(machine);
        // An ANSI-saved file: é is 0xE9 in Windows-1252, which isn't valid UTF-8. The reference's strict
        // read_text("utf-8") raised on either file and the Overview never loaded (T-1, bug #86).
        File.WriteAllBytes(machine.PathOf(@"Game\Fallout4.ccc"), [.. "ccDup.esm\r\ncc"u8, 0xE9, .. ".esl\r\n"u8]);
        File.WriteAllBytes(machine.PathOf(@"LocalAppData\Fallout4\plugins.txt"), [.. "*Unofficial.esp\r\n*"u8, 0xE9, .. "té.esp\r\n"u8]);

        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        // After the game masters: the CC list's one UTF-8 line, then plugins.txt's.
        var names = report.Modules.Enabled.Select(p => p.Name).ToList();
        Assert.Equal(["ccDup.esm", "Unofficial.esp"], names[^2..]);
        Assert.All(names[..^2], n => Assert.EndsWith(".esm", n, StringComparison.OrdinalIgnoreCase));
        var ccc = Assert.Single(report.Problems, p => p.ItemText == "Fallout4.ccc");
        Assert.Equal("Unexpected Format", ccc.Kind);
        Assert.StartsWith("Fallout4.ccc is not valid UTF-8.", ccc.Summary, StringComparison.Ordinal);
        var plugins = Assert.Single(report.Problems, p => p.ItemText == "plugins.txt");
        Assert.Equal("Unexpected Format", plugins.Kind);
        Assert.StartsWith("plugins.txt is not valid UTF-8.", plugins.Summary, StringComparison.Ordinal);
        // Not a missing file: neither warning box is raised.
        Assert.Empty(report.Warnings);
    }

    [Fact]
    [Trait("Parity", "OVW-8")]
    public async Task A_refresh_that_dies_in_the_modules_carries_what_it_had_gathered()
    {
        using var machine = Scenario("overview/modules-archives-mixed");
        var game = await Locate(machine);
        var loaded = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        // The local app data folder goes away, so finding plugins.txt raises mid-modules (as at startup, INI-1). This
        // used to be shown by an archive format that isn't UTF-8, which the port now reports instead (bug #86).
        Directory.Delete(machine.PathOf("LocalAppData"), recursive: true);

        var failure = Assert.Throws<OverviewIncompleteException>(() => OverviewReport.Gather(game, machine.Host, new CapturingLogger(), loaded));

        Assert.IsType<FileNotFoundException>(failure.InnerException);
        var partial = failure.Partial;
        Assert.Equal(loaded.InstallType, partial.InstallType);
        // The masters and the CC modules had been listed; plugins.txt's never were.
        Assert.InRange(partial.Modules.Enabled.Count, 1, loaded.Modules.Enabled.Count - 1);
        // The archives never started, so they are still the previous report's.
        Assert.Same(loaded.Archives, partial.Archives);
        // The frames were never built, so neither were the problems they append.
        Assert.DoesNotContain(partial.Problems, p => p.Kind is ProblemTypes.WrongVersion or "Limit Exceeded");
    }

    [Fact]
    [Trait("Parity", "OVW-C2"), Deviation(33, "Each enabled module is counted once, so the module limits are reached by distinct modules, not by one module listed many times.")]
    public async Task Module_limits_count_distinct_modules_and_a_module_listed_again_once()
    {
        // overview/limits-exceeded reached the module limits by listing Full.esp and Light.esl thousands of times,
        // which the reference counted each time (B-17). The port counts each module once (bug #33), so the limits
        // are reached here with distinct modules, each also listed a second time.
        using var machine = Scenario("overview/limits-exceeded");
        var game = await Locate(machine);
        var data = machine.PathOf(@"Game\Data");
        var listed = new List<string> { "Full.esp", "Light.esl" };
        for (var i = 0; i < 254; i++)
        {
            File.Copy(Path.Combine(data, "Full.esp"), Path.Combine(data, $"Full{i:D3}.esp"));
            listed.Add($"Full{i:D3}.esp");
        }

        for (var i = 0; i < 4096; i++)
        {
            File.Copy(Path.Combine(data, "Light.esl"), Path.Combine(data, $"Light{i:D4}.esl"));
            listed.Add($"Light{i:D4}.esl");
        }

        // Every module twice, the second time in other case: still one module each (T-8).
        var lines = listed.Select(n => "*" + n).Concat(listed.Select(n => "*" + n.ToUpperInvariant()));
        File.WriteAllLines(machine.PathOf(@"LocalAppData\Fallout4\plugins.txt"), lines);

        var report = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        Assert.Equal(4352, report.Modules.Enabled.Count);
        Assert.Equal(new LimitedCount(255, 254), report.FullModules);
        Assert.Equal(new LimitedCount(4097, 4096), report.LightModules);
        Assert.Equal(CountLevel.Exceeded, report.TotalModules.Level);
        var full = Assert.Single(report.Problems, p => p.ItemText == "255 Full Modules");
        Assert.Equal("Limit Exceeded", full.Kind);
        Assert.Equal("You have 255 Full Modules enabled. The limit is 254.", full.Summary);
        var light = Assert.Single(report.Problems, p => p.ItemText == "4097 Light Modules");
        Assert.Equal("You have 4097 Light Modules enabled. The limit is 4096.", light.Summary);
    }

    [Theory]
    [Trait("Parity", "OVW-C1")]
    [InlineData(242, 256, CountLevel.Good)]
    [InlineData(243, 256, CountLevel.Warning)]
    [InlineData(256, 256, CountLevel.Warning)]
    [InlineData(257, 256, CountLevel.Exceeded)]
    [InlineData(241, 255, CountLevel.Good)]
    [InlineData(242, 255, CountLevel.Warning)]
    [InlineData(240, 254, CountLevel.Good)]
    [InlineData(241, 254, CountLevel.Warning)]
    [InlineData(4131, 4350, CountLevel.Good)]
    [InlineData(4132, 4350, CountLevel.Warning)]
    [InlineData(971, 1024, CountLevel.Good)]
    [InlineData(972, 1024, CountLevel.Warning)]
    [InlineData(1025, 1024, CountLevel.Exceeded)]
    [InlineData(1943, 2047, CountLevel.Good)]
    [InlineData(1944, 2047, CountLevel.Warning)]
    [InlineData(0, 4096, CountLevel.Good)]
    public void Counts_turn_orange_at_95_percent_truncated_and_red_past_the_limit(int count, int limit, CountLevel expected)
        // The marks are Python's int(0.95 * limit): 243 of 256, 242 of 255, 241 of 254, 4132 of 4350, 972 of 1024 and
        // 1944 of 2047, worked out by hand from the reference's formula.
        => Assert.Equal(expected, new LimitedCount(count, limit).Level);

    [Theory]
    [Trait("Parity", "T-2")]
    [InlineData(InstallType.Anniversary, 1024, 1023)]
    [InlineData(InstallType.NextGen, 256, 255)]
    [InlineData(InstallType.OldGen, 256, 255)]
    [InlineData(InstallType.DownGrade, 256, 255)]
    [InlineData(InstallType.Obsolete, 256, 255)]
    [InlineData(InstallType.Unknown, 256, 255)]
    public void Only_Anniversary_raises_the_archive_limits(InstallType type, int general, int texture)
    {
        Assert.Equal(general, OverviewLimits.GeneralArchiveLimit(type));
        Assert.Equal(texture, OverviewLimits.TextureArchiveLimit(type));
    }

    /// <summary>The archive's set entry is spelled as the INI suffix built it (<c>Fo76 - main.ba2</c>), not as on disk.</summary>
    private static bool IsFo76(string name) => string.Equals(name, "Fo76 - Main.ba2", StringComparison.OrdinalIgnoreCase);

    private static MaterializedScenario Scenario(string id) => ParityScenarios.All().Single(s => s.Id == id).Materialize();

    private static async Task<GameInstall> Locate(MaterializedScenario machine)
        => await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
           ?? throw new InvalidOperationException("The scenario's game wasn't found.");
}
