using Avalonia.Headless.XUnit;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The app keeps <c>settings.json</c>, <c>cm-toolkit.log</c> and its Delta Patches in its own folder, not the working
/// directory (bug #36, fixed as a Deviation, ADR-0006): startup's default settings path, the folder the session hands
/// the Downgrader, and the working directory's <c>settings.json</c> copied into an app folder that has none. The
/// <c>%LOCALAPPDATA%</c> fallback and the writability probe are in <c>Core/AppFolderFilesTests</c>.
/// </summary>
public sealed class AppFolderTests
{
    [Fact]
    [Trait("Parity", "SHELL-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public void Outside_a_scenario_run_the_app_folder_is_the_exe_folder_when_it_is_writable()
    {
        // The test host never sets the scenario variables, which only the screenshot and smoke scripts do.
        Assert.False(CMToolkit.App.Parity.ScenarioHostEnvironment.IsRequested);
        // The test's bin folder, like a user's extracted Tools\CM-Toolkit, can be written to.
        Assert.True(AppFolderFiles.IsWritable(AppContext.BaseDirectory));

        var choice = AppFolder.ForThisRun();

        Assert.Equal(AppContext.BaseDirectory, choice.Folder);
        Assert.Null(choice.UnwritableExeFolder);
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public async Task Startup_copies_the_working_directorys_settings_into_an_app_folder_without_any_and_loads_them_there()
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

        // No settings.json in the app folder: the working directory's, where the reference kept it, is copied there and
        // loaded from there (its update_source is none, where the defaults would say nexus); the original stays put.
        Assert.NotNull(window);
        Assert.Equal(cwdText, File.ReadAllText(Path.Combine(appFolder, "settings.json")));
        Assert.Equal(UpdateSource.None, built!.Model.Settings.Current.UpdateSource);
        Assert.Equal(cwdText, File.ReadAllText(cwdSettings));
        Assert.Equal(appFolder, built.AppFolder);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-1")]
    [Deviation(36, "settings.json, cm-toolkit.log and downloaded Delta Patches live in the exe's folder, or a CM-Toolkit folder in %LOCALAPPDATA% when it isn't writable, never the working directory, whose settings.json is copied over once when the app folder has none.")]
    public async Task Startup_keeps_the_app_folders_own_settings_over_the_working_directorys()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "shell/main-window").Materialize();
        var cwdSettings = machine.PathOf("Cwd/settings.json");
        using var dir = new TempDirectory();
        var appFolder = Path.Combine(dir.Path, "App");
        // A DEBUG level, which the working directory's file (INFO) doesn't have, so it shows which one loaded.
        var ownText = File.ReadAllText(cwdSettings).Replace("\"INFO\"", "\"DEBUG\"", StringComparison.Ordinal);
        Directory.CreateDirectory(appFolder);
        File.WriteAllText(Path.Combine(appFolder, "settings.json"), ownText);
        using var log = AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now);
        var host = new FakeHostEnvironment(machine.Host.Block with { AppDir = appFolder }, machine.Root);
        var sequence = new StartupSequence(new AppRuntime(log), host, log, new WindowStartupDialogs()) { AppFolder = appFolder };

        var window = await sequence.BuildMainWindowAsync().WithTimeout();

        Assert.NotNull(window);
        Assert.Equal(ownText, File.ReadAllText(Path.Combine(appFolder, "settings.json")));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Debug, log.MinimumLevel);
        window.Close();
    }
}
