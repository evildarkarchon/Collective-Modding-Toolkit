using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CMToolkit.App;
using CMToolkit.App.Parity;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Views;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// How the Downgrader is reached and laid out: both Downgrade Manager buttons open it over the main window from the
/// real startup composition (OVW-B9, TOOL-1), and a long version label pushes Patch All and About out of the fixed
/// window, as in the reference.
/// </summary>
public sealed class DowngraderWindowTests
{
    [AvaloniaFact]
    [Trait("Parity", "OVW-B9")]
    public void The_overview_downgrade_manager_button_opens_the_downgrader_over_the_main_window()
    {
        using var app = new ComposedApp("downgrader/og-steam");

        MouseClick.On(app.Named<Button>("DowngradeManager"));

        var downgrader = app.WaitForDowngrader();
        Assert.Same(app.Window, downgrader.Owner);
        Assert.Equal("Old-Gen", app.Named<TextBlock>(downgrader, "Game0").Text);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void The_tools_tab_downgrade_manager_button_opens_the_downgrader_over_the_main_window()
    {
        using var app = new ComposedApp("downgrader/og-steam");
        var tabs = app.Window.GetLogicalDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        DispatcherWait.Until(() => app.Window.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Downgrade Manager"));
        app.Window.UpdateLayout();

        MouseClick.On(app.Window.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == "Downgrade Manager"));

        var downgrader = app.WaitForDowngrader();
        Assert.Same(app.Window, downgrader.Owner);
    }

    /// <summary>
    /// The reference's grid gives column 2 (Patch All, About) all the spare width of a fixed 600 px window, so when
    /// <c>Next-Gen &amp; Anniversary</c> widens the first column past it, Tk shrinks that column to nothing and the
    /// buttons can't be seen or clicked (screenshot pair <c>downgrader/dg-game</c>). Bug #85: reproduced, not fixed (ADR-0001).
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "DG-2")]
    public void The_patch_and_about_buttons_get_no_width_when_a_long_label_overflows_the_window()
    {
        using var app = new ComposedApp("downgrader/dg-game");

        MouseClick.On(app.Named<Button>("DowngradeManager"));

        var downgrader = app.WaitForDowngrader();
        Assert.Equal("Next-Gen & Anniversary", app.Named<TextBlock>(downgrader, "Game2").Text);
        Assert.Equal(0, app.Named<Button>(downgrader, "PatchAll").Bounds.Width);
        Assert.Equal(0, app.Named<Button>(downgrader, "About").Bounds.Width);
    }

    [AvaloniaFact]
    [Trait("Parity", "DG-2")]
    public void The_patch_and_about_buttons_fill_the_last_column_when_the_labels_fit()
    {
        using var app = new ComposedApp("downgrader/og-steam");

        MouseClick.On(app.Named<Button>("DowngradeManager"));

        var downgrader = app.WaitForDowngrader();
        Assert.True(app.Named<Button>(downgrader, "PatchAll").Bounds.Width > 100);
        Assert.Equal(app.Named<Button>(downgrader, "PatchAll").Bounds.Width, app.Named<Button>(downgrader, "About").Bounds.Width);
    }

    /// <summary>
    /// The app as startup composes it (the real <see cref="StartupSequence"/>, so the session's main window and the
    /// tabs' openers are the production wiring) on a scenario's machine, shown with its Overview loaded.
    /// </summary>
    private sealed class ComposedApp : IDisposable
    {
        private readonly MaterializedScenario _machine;
        private readonly TempDirectory _dir = new();
        private readonly LogFile _log;

        public ComposedApp(string scenarioId)
        {
            var scenario = ParityScenarios.All().Single(s => s.Id == scenarioId);
            _machine = scenario.Materialize();
            // The exe's own reading of a scenario's host block, which supplies the app folder startup reads (the
            // Download Source) where the block names none.
            var host = ScenarioHostEnvironment.Parse(File.ReadAllText(Path.Combine(scenario.Directory, "scenario.json")), _machine.Root);
            _log = AppStartup.OpenLog(Path.Combine(_dir.Path, "cm-toolkit.log"), DateTime.Now);
            var sequence = new StartupSequence(new AppRuntime(_log), host, _log, new ScriptedStartupDialogs([], _machine.Host.Expand))
            {
                SettingsPath = Path.Combine(_machine.Host.Expand(_machine.Host.Block.Cwd!), "settings.json"),
            };
            var building = sequence.BuildMainWindowAsync();
            DispatcherWait.Until(() => building.IsCompleted);
            Window = building.GetAwaiter().GetResult() ?? throw new InvalidOperationException("Startup exited.");
            Window.Show();
            DispatcherWait.Until(() => Window.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "DowngradeManager"));
            Window.UpdateLayout();
        }

        public MainWindow Window { get; }

        public T Named<T>(string name)
            where T : Control
            => Named<T>(Window, name);

        public T Named<T>(Window window, string name)
            where T : Control
            => window.GetLogicalDescendants().OfType<T>().Where(c => c.Name == name).Distinct().Single();

        /// <summary>The Downgrader the click opened, once its detection has run and it is built.</summary>
        public DowngraderWindow WaitForDowngrader()
        {
            DispatcherWait.Until(() => Window.OwnedWindows.OfType<DowngraderWindow>().SingleOrDefault() is { Versions: not null, Content: not null });
            var downgrader = Window.OwnedWindows.OfType<DowngraderWindow>().Single();
            downgrader.UpdateLayout();
            return downgrader;
        }

        public void Dispose()
        {
            Window.Close();
            _log.Dispose();
            _machine.Dispose();
            _dir.Dispose();
        }
    }
}
