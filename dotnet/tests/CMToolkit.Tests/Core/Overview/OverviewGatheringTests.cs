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
/// the previous report's later parts; and the count colours on every limit's boundaries (OVW-C1).
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
    [Trait("Parity", "OVW-A4")]
    public async Task A_refresh_that_dies_in_the_archives_carries_what_it_had_gathered()
    {
        using var machine = Scenario("overview/modules-archives-mixed");
        var game = await Locate(machine);
        var loaded = OverviewReport.Gather(game, machine.Host, new CapturingLogger());

        // The format bytes of an enabled archive stop being UTF-8, so formatting its problem raises (T-1).
        var archive = machine.PathOf(@"Game\Data\Fo76 - Main.ba2");
        var bytes = File.ReadAllBytes(archive);
        bytes.AsSpan(8, 4).Fill(0xFF);
        File.WriteAllBytes(archive, bytes);

        var failure = Assert.Throws<OverviewIncompleteException>(() => OverviewReport.Gather(game, machine.Host, new CapturingLogger(), loaded));

        Assert.IsType<System.Text.DecoderFallbackException>(failure.InnerException);
        var partial = failure.Partial;
        Assert.Equal(loaded.InstallType, partial.InstallType);
        Assert.Equal(loaded.Modules.Enabled.Count, partial.Modules.Enabled.Count);
        // Added to the unreadable set before the problem text was formatted, and the problem itself never added.
        Assert.Contains(partial.Archives.Unreadable, p => IsFo76(p.Name));
        Assert.DoesNotContain(partial.Problems, p => IsFo76(p.ItemText));
        // The frames were never built, so neither were the problems they append.
        Assert.DoesNotContain(partial.Problems, p => p.Kind is ProblemTypes.WrongVersion or "Limit Exceeded");
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
