using Avalonia;
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
/// real startup composition (OVW-B9, TOOL-1), and a long version label widens the window instead of pushing Patch All
/// and About out of it, as the reference's fixed window did (bug #85, fixed).
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
    /// buttons can't be seen or clicked (screenshot pair <c>downgrader/dg-game</c>). Bug #85 is fixed (ADR-0006): the
    /// port's window grows to the width its grid asks for, so both buttons keep their requested width, inside the window.
    /// </summary>
    [AvaloniaFact]
    [Trait("Parity", "DG-2"), Deviation(85, "The Downgrader widens past 600 px to fit a long version label, so Patch All and About stay visible.")]
    public void The_window_widens_to_keep_the_patch_and_about_buttons_when_a_long_label_overflows_600_px()
    {
        using var app = new ComposedApp("downgrader/dg-game");

        MouseClick.On(app.Named<Button>("DowngradeManager"));

        var downgrader = app.WaitForDowngrader();
        Assert.Equal("Next-Gen & Anniversary", app.Named<TextBlock>(downgrader, "Game2").Text);
        Assert.True(downgrader.Width > 600);
        foreach (var name in new[] { "PatchAll", "About" })
        {
            var button = app.Named<Button>(downgrader, name);
            Assert.True(button.Bounds.Width > 30, $"{name} is {button.Bounds.Width} px wide.");
            // Its right edge (plus its 10 px padx) lies inside the window's client area.
            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), downgrader)!.Value.X;
            Assert.True(right + 10 <= downgrader.Width + 0.5, $"{name} ends at {right}, past {downgrader.Width}.");
        }

        // Clicked with the mouse, About opens: the button is really there to click.
        MouseClick.On(app.Named<Button>(downgrader, "About"));
        DispatcherWait.Until(() => downgrader.OwnedWindows.OfType<AboutWindow>().Any());
        downgrader.OwnedWindows.OfType<AboutWindow>().Single().Close();
    }

    [AvaloniaFact]
    [Trait("Parity", "DG-2")]
    public void The_window_keeps_its_600_px_when_the_labels_fit()
    {
        using var app = new ComposedApp("downgrader/og-steam");

        MouseClick.On(app.Named<Button>("DowngradeManager"));

        Assert.Equal(600, app.WaitForDowngrader().Width);
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
    /// Showing the Downgrader over a main window that has closed throws before its detection starts, so no runner reports
    /// it. It reaches the Error Window the moment Open is called, not whenever (if ever) the garbage collector finds the
    /// task unobserved.
    /// </summary>
    [AvaloniaFact]
    public void Opening_over_a_closed_main_window_reports_the_error_at_once()
    {
        using var machine = ParityScenarios.All().Single(s => s.Id == "downgrader/og-steam").Materialize();
        var logger = new CapturingLogger();
        var (main, session) = DowngraderRunScenarioTests.Compose(machine, logger, new DowngraderRunScenarioTests.HeldHandler(null));
        main.Close();

        DowngraderWindow.Open(session);

        Assert.Single(logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"{ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: ", session.Runtime.Errors.Window?.Text);
        Assert.Empty(main.OwnedWindows);
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
