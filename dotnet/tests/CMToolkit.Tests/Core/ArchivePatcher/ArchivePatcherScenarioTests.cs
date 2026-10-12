using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core;
using CMToolkit.Core.ArchivePatcher;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.ArchivePatcher;

/// <summary>
/// The <c>archive-patcher/*</c> scenarios (the real <c>ArchivePatcher</c> over a loaded Overview, driven through the
/// driver's fixed script) replayed against <see cref="ArchivePatching"/> and the Overview gathering it refreshes with:
/// after each step, the target, the name filter and the files Patch All would patch (AP-2), and after each Patch All
/// the Data folder's bytes (AP-3). The window, the log pane and the log lines are the App test's. The scenarios cite
/// their IDs.
/// </summary>
public sealed class ArchivePatcherScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("archive-patcher", "archive-patcher");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var game = await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
                   ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        var logger = new CapturingLogger();
        var report = OverviewReport.Gather(game, machine.Host, logger);
        var changes = ArchivePatcherGolden.ApplyChanges(machine);

        var target = ArchiveVersion.OldGen;
        string? filter = null;
        var steps = new JsonArray();

        JsonObject Step(string name, bool patched = false)
        {
            var step = new JsonObject
            {
                ["step"] = name,
                ["desiredVersion"] = (int)target,
                ["nameFilter"] = filter,
                ["files"] = ArchivePatcherGolden.Unordered(machine, ArchivePatching.FilesToPatch(report.Archives, target, filter)),
            };
            if (patched)
            {
                step["data"] = ArchivePatcherGolden.DataSnapshot(machine, game.DataPath!.ToString());
            }

            return step;
        }

        void PatchAll()
        {
            var files = ArchivePatching.FilesToPatch(report.Archives, target, filter);
            ArchivePatching.Patch(files, target, filter, machine.Host, logger, new Progress<OperationEvent>());
            // The App refreshes the Overview after each run (refresh_tab), and the next list comes from its report.
            report = OverviewReport.Gather(game, machine.Host, logger, report);
        }

        steps.Add(Step("opened"));
        PatchAll();
        steps.Add(Step("patched", patched: true));
        target = ArchiveVersion.NextGen;
        steps.Add(Step("v8"));
        filter = ArchivePatching.NameFilter(ArchivePatcherGolden.Filter);
        steps.Add(Step("filtered"));
        PatchAll();
        steps.Add(Step("patchedFiltered", patched: true));
        filter = ArchivePatching.NameFilter(string.Empty);
        steps.Add(Step("unfiltered"));

        var actual = new JsonObject { ["archivePatcher"] = new JsonObject { ["changes"] = changes, ["steps"] = steps } };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
    }
}
