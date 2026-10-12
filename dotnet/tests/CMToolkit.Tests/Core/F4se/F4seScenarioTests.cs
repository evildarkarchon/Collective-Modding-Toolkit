using System.Text.Json.Nodes;
using CMToolkit.App.Startup;
using CMToolkit.Core.F4se;
using CMToolkit.Core.Overview;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.Core.F4se;

/// <summary>
/// The <c>f4se/*</c> scenarios (recorded from the real <c>CMChecker</c>: the Overview loaded, then the F4SE tab
/// selected) replayed against <see cref="F4seScan.Run"/> and <see cref="F4seVerdict.Of"/>: the load error or the
/// failure, each DLL's inspection, and the semantic verdict of each row's cells for the Overview's Install Type. The
/// rendered side (<c>display</c>) is the App test's. The scenarios cite their IDs.
/// </summary>
public sealed class F4seScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("f4se", "f4se-tab");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var game = await GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand))
                   ?? throw new InvalidOperationException("The scenario's game wasn't found.");
        // The Overview loads first, as the initially selected tab, and sets the Install Type the verdicts read.
        var installType = OverviewReport.Gather(game, machine.Host, new CapturingLogger()).InstallType;

        F4seScanResult? scan = null;
        JsonObject? failure = null;
        try
        {
            scan = F4seScan.Run(game, machine.Host, new CapturingLogger());
        }
        catch (F4sePluginLoadException)
        {
            // B-18: the reference's OSError out of the load, recorded by kind only. Since bug #34 was fixed the port
            // lists the DLL instead, so its port goldens expect no failure and this would show as a mismatch.
            failure = new JsonObject { ["kind"] = "os" };
        }

        var loaded = scan is { LoadingError: null };
        var actual = new JsonObject
        {
            ["f4se"] = new JsonObject
            {
                ["installType"] = installType.DisplayName(),
                ["loaded"] = loaded,
                ["loadingError"] = scan?.LoadingError,
                ["failure"] = failure,
                ["dlls"] = loaded ? new JsonArray([.. scan!.Dlls.Select(d => (JsonNode)ProjectInfo(d))]) : null,
                ["verdicts"] = loaded
                    ? new JsonArray([.. scan!.Dlls.Select(d => (JsonNode)ProjectVerdict(d.Name, F4seVerdict.Of(d.Info, installType)))])
                    : null,
            },
        };
        GoldenAssert.Matches(scenario.LoadGolden(), actual, GoldenCompareMode.Semantic);
    }

    /// <summary>
    /// One DLL's inspection as the driver records <c>dll_info</c>. A DLL the loader refused (bug #34's fix) has every
    /// fact null, as its port goldens write it: the reference never got that far.
    /// </summary>
    internal static JsonObject ProjectInfo(F4seDll dll) => new()
    {
        ["name"] = dll.Name,
        ["isF4se"] = dll.Info?.IsF4se,
        ["supportsOg"] = dll.Info?.SupportsOg,
        ["supportsNgAe"] = dll.Info?.SupportsNgAe,
        ["supportsNg"] = dll.Info?.SupportsNg,
        ["supportsAe"] = dll.Info?.SupportsAe,
        ["addressIndependent"] = dll.Info?.AddressIndependent,
        ["structureIndependent"] = dll.Info?.StructureIndependent,
    };

    /// <summary>One row's verdicts as the driver reads them off the Treeview's glyphs.</summary>
    internal static JsonObject ProjectVerdict(string name, F4seVerdict verdict) => new()
    {
        ["name"] = name,
        ["og"] = Name(verdict.Og),
        ["ng"] = Name(verdict.Ng),
        ["ae"] = Name(verdict.Ae),
        ["yourGame"] = verdict.YourGame is { } yourGame ? Name(yourGame) : null,
    };

    private static string Name(F4seSupport support) => support switch
    {
        F4seSupport.Unknown => "unknown",
        F4seSupport.Unsupported => "unsupported",
        F4seSupport.Supported => "supported",
        F4seSupport.Note => "note",
        F4seSupport.Independent => "independent",
        _ => throw new ArgumentOutOfRangeException(nameof(support), support, null),
    };
}
