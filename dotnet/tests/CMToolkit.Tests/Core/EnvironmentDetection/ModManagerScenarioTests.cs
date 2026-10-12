using System.Text;
using System.Text.Json.Nodes;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.Core.EnvironmentDetection;

/// <summary>
/// MM-1 and MO2-1..MO2-9, MO2-11: the <c>environment/mm-*</c> and <c>environment/mo2-*</c> scenarios, recorded from
/// <c>utils.find_mod_manager()</c> plus the mod manager part of <c>GameInfo.find_path</c>, replayed against
/// <see cref="ModManagerDetection.Find"/> and <see cref="Mo2ConfigurationReader.Locate"/>. The scenarios cite their IDs
/// (and T-1, T-7, B-5, B-6) in <c>scenario.json</c>.
/// </summary>
public sealed class ModManagerScenarioTests
{
    public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("environment", "mod-manager");

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Core_matches_the_reference(ParityScenario scenario)
    {
        using var machine = scenario.Materialize();
        var golden = new JsonObject { ["manager"] = null };

        ModManager? manager;
        try
        {
            manager = ModManagerDetection.Find(machine.Host);
        }
        catch (Exception ex)
        {
            golden["error"] = ProjectError(machine, ex);
            GoldenAssert.Matches(scenario.LoadGolden(), golden, GoldenCompareMode.Semantic);
            return;
        }

        if (manager is not null)
        {
            golden["manager"] = new JsonObject
            {
                ["name"] = manager.Name,
                ["exePath"] = PathText(machine, manager.ExePath),
                ["version"] = manager.VersionText,
            };
            try
            {
                var mo2 = Mo2ConfigurationReader.Locate(manager, machine.Host);
                golden["mo2"] = mo2 is null ? null : Project(machine, mo2);
            }
            catch (Exception ex)
            {
                golden["error"] = ProjectError(machine, ex);
            }
        }

        GoldenAssert.Matches(scenario.LoadGolden(), golden, GoldenCompareMode.Semantic);
    }

    private static JsonObject Project(MaterializedScenario machine, Mo2Configuration mo2)
    {
        var settings = new JsonArray();
        foreach (var setting in mo2.Settings)
        {
            settings.Add(new JsonObject
            {
                ["key"] = setting.Key,
                ["value"] = setting.Value switch
                {
                    Mo2SettingValue.PathValue p => new JsonObject { ["path"] = PathText(machine, p.Path) },
                    Mo2SettingValue.TextValue t => new JsonObject { ["text"] = t.Text },
                    Mo2SettingValue.FlagValue f => f.Flag,
                    Mo2SettingValue.ListValue l => new JsonArray([.. l.Items.Select(i => JsonValue.Create(i))]),
                    _ => throw new ArgumentOutOfRangeException(nameof(mo2), setting.Value, null),
                },
            });
        }

        return new JsonObject
        {
            ["iniPath"] = PathText(machine, mo2.IniPath),
            ["portable"] = mo2.Portable,
            ["portableTxtPath"] = PathText(machine, mo2.PortableTxtPath),
            ["gamePath"] = PathText(machine, mo2.GamePath),
            ["selectedProfile"] = mo2.SelectedProfile,
            ["stagePath"] = PathText(machine, mo2.StagePath),
            ["overwritePath"] = PathText(machine, mo2.OverwritePath),
            ["profilesPath"] = PathText(machine, mo2.ProfilesPath),
            ["skipFileSuffixes"] = new JsonArray([.. mo2.SkipFileSuffixes.Select(s => JsonValue.Create(s))]),
            ["skipDirectories"] = new JsonObject
            {
                [GoldenComparer.UnorderedKey] = new JsonArray([.. mo2.SkipDirectories.Select(s => JsonValue.Create(s))]),
            },
            ["settings"] = settings,
        };
    }

    /// <summary>
    /// The driver's <c>_path_text</c>: <c>str(path)</c> with the temp root as <c>&lt;ROOT&gt;</c>, and the root's drive
    /// as <c>&lt;DRIVE&gt;</c> on a path outside the root (B-6 lands on the drive root).
    /// </summary>
    private static string? PathText(MaterializedScenario machine, object? path)
    {
        if (path is null)
        {
            return null;
        }

        var text = path.ToString()!
            .Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal)
            .Replace(machine.Root.Replace('\\', '/'), MaterializedScenario.RootToken, StringComparison.Ordinal);
        var drive = machine.Root[..2];
        return !text.StartsWith(MaterializedScenario.RootToken, StringComparison.Ordinal) && text.StartsWith(drive, StringComparison.Ordinal)
            ? "<DRIVE>" + text[drive.Length..]
            : text;
    }

    /// <summary>
    /// The driver's <c>_error</c>: the kind of failure, plus the message for the ones the reference wrote for the user.
    /// </summary>
    private static JsonObject ProjectError(MaterializedScenario machine, Exception ex) => ex switch
    {
        DecoderFallbackException => new JsonObject { ["kind"] = "decode" },
        UnauthorizedAccessException => new JsonObject { ["kind"] = "accessDenied" },
        FileNotFoundException => new JsonObject { ["kind"] = "fileNotFound", ["message"] = ex.Message },
        Mo2ConfigurationException => new JsonObject
        {
            ["kind"] = "value",
            ["message"] = ex.Message.Replace(machine.Root, MaterializedScenario.RootToken, StringComparison.Ordinal),
        },
        _ => throw new InvalidOperationException("Unexpected failure", ex),
    };

    /// <summary>MM-1: a manager's name and three-part version, with all zeros standing for a missing resource (T-4).</summary>
    [Theory]
    [Trait("Parity", "MM-1")]
    [InlineData(new[] { 2, 5, 2, 7 }, "2.5.2")]
    [InlineData(new[] { 0, 0, 0, 0 }, "0.0.0")]
    [InlineData(null, "0.0.0")]
    [InlineData(new[] { 1, 0, 0, 0 }, "1.0.0")]
    public void The_version_is_the_first_three_parts(int[]? fileVersion, string expected)
    {
        var host = new FakeHostEnvironment(
            new HostBlock(Processes: [new HostProcess("ModOrganizer.exe", @"C:\MO2\ModOrganizer.exe", fileVersion)]),
            Path.GetTempPath());

        var manager = ModManagerDetection.Find(host);

        Assert.NotNull(manager);
        Assert.Equal("Mod Organizer", manager.Name);
        Assert.Equal(expected, manager.VersionText);
        Assert.Equal(@"C:\MO2\ModOrganizer.exe", manager.ExePath.ToString());
    }
}
