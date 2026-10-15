using Avalonia.Headless.XUnit;
using CMToolkit.App;
using CMToolkit.App.Parity;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Core.Updates;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The update check as the app wires it into startup (<see cref="UpdateBanner.StartupHook"/>): run with the loaded
/// settings' <c>update_source</c>, before the main window is shown, with the banner already in place (UPD-1,
/// SHELL-7); and a response the reference didn't survive failing startup (SHELL-13). The machine is the
/// <c>shell/main-window</c> scenario's; the HTTP is an <c>update/*</c> scenario's canned responses.
/// </summary>
public sealed class UpdateStartupTests
{
    [AvaloniaFact]
    [Trait("Parity", "UPD-1")]
    [Trait("Parity", "SHELL-7")]
    public async Task The_banner_is_in_row_zero_before_the_main_window_first_shows()
    {
        using var startup = new UpdateStartup("update/nexus-newer");

        var window = await startup.Sequence.BuildMainWindowAsync().WithTimeout();

        Assert.NotNull(window);
        Assert.False(window.IsVisible);
        var banner = Assert.IsType<TkGrid>(window.UpdateBanner);
        Assert.Equal(0, Avalonia.Controls.Grid.GetRow((Avalonia.Controls.Control)banner.Parent!));
        Assert.Single(startup.Http.Requests);
    }

    [AvaloniaFact]
    [Trait("Parity", "UPD-1")]
    public async Task With_no_newer_version_there_is_no_banner()
    {
        using var startup = new UpdateStartup("update/nexus-older");

        var window = await startup.Sequence.BuildMainWindowAsync().WithTimeout();

        Assert.Null(window!.UpdateBanner);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-13"), Deviation(81, "The port logs any update-check failure and treats it as no update, so startup continues.")]
    public async Task A_response_the_reference_did_not_survive_is_logged_and_startup_continues()
    {
        // The reference's KeyError escaped CMChecker.__init__ and failed startup; the port logs the failed check and
        // opens the main window without a banner (bug #81, fixed per ADR-0006).
        using var startup = new UpdateStartup("update/github-missing-tag");

        var result = await AppStartup.BuildAsync(startup.Runtime, async () => await startup.Sequence.BuildMainWindowAsync())
            .WithTimeout();

        Assert.False(result.Failed);
        var window = Assert.IsType<MainWindow>(result.Window);
        Assert.Null(window.UpdateBanner);
    }

    /// <summary>
    /// The startup sequence on <c>shell/main-window</c>'s machine (through the exe's own scenario host, whose exe folder
    /// is the test output's), with <paramref name="updateScenario"/>'s settings file and canned HTTP.
    /// </summary>
    private sealed class UpdateStartup : IDisposable
    {
        private readonly MaterializedScenario _machine;
        private readonly TempDirectory _dir = new();
        private readonly LogFile _log;

        public UpdateStartup(string updateScenario)
        {
            var shell = ParityScenarios.All().Single(s => s.Id == "shell/main-window");
            var update = ParityScenarios.All().Single(s => s.Id == updateScenario);
            _machine = shell.Materialize();
            var host = ScenarioHostEnvironment.Parse(File.ReadAllText(Path.Combine(shell.Directory, "scenario.json")), _machine.Root);
            var settingsPath = Path.Combine(_dir.Path, "settings.json");
            File.Copy(Path.Combine(update.Directory, "tree", "Cwd", "settings.json"), settingsPath);
            _log = AppStartup.OpenLog(Path.Combine(_dir.Path, "cm-toolkit.log"), DateTime.Now);
            Runtime = new AppRuntime(_log) { Browser = new RecordingBrowser() };
            Http = new ParityHttpHandler(Path.Combine(update.Directory, "http"));
            Sequence = new StartupSequence(Runtime, host, _log, new WindowStartupDialogs())
            {
                SettingsPath = settingsPath,
                // As the reference ran when the update/* goldens were recorded: which canned release is newer hinges on it.
                CheckForUpdatesAsync = UpdateBanner.StartupHook(Http, ParityScenario.RecordedAppVersion),
            };
        }

        public AppRuntime Runtime { get; }

        public ParityHttpHandler Http { get; }

        public StartupSequence Sequence { get; }

        public void Dispose()
        {
            _log.Dispose();
            _dir.Dispose();
            _machine.Dispose();
        }
    }
}
