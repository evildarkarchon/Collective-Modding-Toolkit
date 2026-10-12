using Avalonia.Headless.XUnit;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The app keeps <c>settings.json</c>, <c>cm-toolkit.log</c> and its Delta Patches in its own folder, not the working
/// directory (bug #36, fixed as a Deviation, ADR-0006): startup's default settings path, and the folder the session
/// hands the Downgrader.
/// </summary>
public sealed class AppFolderTests
{
    [Fact]
    [Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    public void Outside_a_scenario_run_the_app_folder_is_the_exe_folder()
    {
        // The test host never sets the scenario variables, which only the screenshot and smoke scripts do.
        Assert.False(CMToolkit.App.Parity.ScenarioHostEnvironment.IsRequested);

        Assert.Equal(AppContext.BaseDirectory, AppFolder.ForThisRun());
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches are resolved against the exe's folder, not the working directory.")]
    public async Task Startup_loads_and_saves_settings_in_the_app_folder_not_the_working_directory()
    {
        // shell/main-window keeps a settings.json in its working directory, as the reference would have read it.
        using var machine = ParityScenarios.All().Single(s => s.Id == "shell/main-window").Materialize();
        var cwdSettings = machine.PathOf("Cwd/settings.json");
        var cwdText = File.ReadAllText(cwdSettings);
        using var dir = new TempDirectory();
        var appFolder = Path.Combine(dir.Path, "App");
        Directory.CreateDirectory(appFolder);
        using var log = AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now);
        AppSession? built = null;
        // The exe's folder is the app folder, as on a user's machine; it has no assets, so the Download Source is nexus.
        var host = new FakeHostEnvironment(machine.Host.Block with { AppDir = appFolder }, machine.Root);
        var sequence = new StartupSequence(new AppRuntime(log), host, log, new WindowStartupDialogs())
        {
            AppFolder = appFolder,
            CheckForUpdatesAsync = (_, session) =>
            {
                built = session;
                return Task.CompletedTask;
            },
        };

        Assert.Equal(Path.Combine(appFolder, "settings.json"), sequence.SettingsPath);
        var window = await sequence.BuildMainWindowAsync().WithTimeout();

        // No settings.json in the app folder: the defaults load and are saved there; the working directory's is ignored.
        Assert.NotNull(window);
        Assert.True(File.Exists(Path.Combine(appFolder, "settings.json")));
        Assert.Equal(cwdText, File.ReadAllText(cwdSettings));
        Assert.Equal(appFolder, built!.AppFolder);
        window.Close();
    }
}
