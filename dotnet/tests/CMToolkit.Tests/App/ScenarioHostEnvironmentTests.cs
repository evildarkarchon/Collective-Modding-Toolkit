using CMToolkit.App.Parity;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;

namespace CMToolkit.Tests.App;

/// <summary>
/// The exe's scenario host (used for screenshot pairs) against the tests' <see cref="FakeHostEnvironment"/>: on every
/// committed scenario's host block they must answer every question the same way, so a screenshot runs the exe on the
/// machine the goldens were recorded on.
/// </summary>
public sealed class ScenarioHostEnvironmentTests
{
    public static TheoryData<string> ScenarioIds => [.. ParityScenarios.All().Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void It_answers_like_the_test_fake(string id)
    {
        var scenario = ParityScenarios.All().Single(s => s.Id == id);
        const string root = @"C:\cmt-parity-root";
        var fake = new FakeHostEnvironment(scenario.Manifest.Host, root);
        var exe = ScenarioHostEnvironment.Parse(File.ReadAllText(Path.Combine(scenario.Directory, "scenario.json")), root);

        Assert.Equal(Answer(() => fake.CurrentDirectory), Answer(() => exe.CurrentDirectory));
        if (scenario.Manifest.Host.AppDir is not null)
        {
            Assert.Equal(fake.AppDirectory, exe.AppDirectory);
        }

        foreach (var folder in Enum.GetValues<KnownFolder>())
        {
            Assert.Equal(Answer(() => fake.GetKnownFolderPath(folder)), Answer(() => exe.GetKnownFolderPath(folder)));
        }

        Assert.Equal(fake.IsWine, exe.IsWine);
        Assert.Equal(Answer(() => fake.OsInfo), Answer(() => exe.OsInfo));
        Assert.Equal(Answer(() => fake.TotalPhysicalMemory), Answer(() => exe.TotalPhysicalMemory));
        Assert.Equal(fake.GetAncestorProcesses(8), exe.GetAncestorProcesses(8));
        foreach (var process in scenario.Manifest.Host.Processes ?? [])
        {
            if (process.Exe is { } processExe)
            {
                Assert.Equal(fake.GetFileVersion(fake.Expand(processExe)), exe.GetFileVersion(exe.Expand(processExe)));
            }
        }

        foreach (var name in (scenario.Manifest.Host.Env?.Keys ?? []).Append("LOCALAPPDATA").Append("NOT_LISTED"))
        {
            Assert.Equal(fake.GetEnvironmentVariable(name), exe.GetEnvironmentVariable(name));
            Assert.Equal(fake.GetEnvironmentVariable(name.ToLowerInvariant()), exe.GetEnvironmentVariable(name.ToLowerInvariant()));
        }

        foreach (var (key, values) in scenario.Manifest.Host.Registry ?? new Dictionary<string, IReadOnlyDictionary<string, RegistryValue>>())
        {
            var separator = key.IndexOf('\\', StringComparison.Ordinal);
            var hive = key[..separator].ToUpperInvariant() is "HKLM" or "HKEY_LOCAL_MACHINE" ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser;
            var path = key[(separator + 1)..];
            foreach (var queried in values.Keys.Append("Not Listed"))
            {
                Assert.Equal(
                    Answer(() => fake.ReadRegistryValue(hive, path.ToLowerInvariant(), queried.ToUpperInvariant())),
                    Answer(() => exe.ReadRegistryValue(hive, path.ToLowerInvariant(), queried.ToUpperInvariant())));
            }

            // The parent key exists but holds no value.
            var parent = path[..path.LastIndexOf('\\')];
            Assert.Equal(Answer(() => fake.ReadRegistryValue(hive, parent, "x")), Answer(() => exe.ReadRegistryValue(hive, parent, "x")));
        }
    }

    [Fact]
    public void Without_the_driver_variables_the_app_runs_on_the_real_machine()
    {
        Assert.Null(Environment.GetEnvironmentVariable(ScenarioHostEnvironment.ScenarioVariable));
        Assert.Null(ScenarioHostEnvironment.FromEnvironmentVariables());
    }

    /// <summary>The value, or the exception's type and message, so a failure compares too.</summary>
    private static object? Answer<T>(Func<T> question)
    {
        try
        {
            return question();
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
