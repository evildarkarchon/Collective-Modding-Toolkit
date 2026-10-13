using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Startup;
using CMToolkit.App.Tabs;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// What the <c>scanner-tab</c> scenarios can't record: the scan's two phases (a blocking Overview refresh, then an
/// interactive worker; THR-1), the Failure Outcomes of a worker that raises (B-3), a refresh that dies, and leaving the
/// tab mid-scan (<c>populate_results</c> raising), and Collapse All / Expand All.
/// </summary>
public sealed class ScannerTabTests
{
    [AvaloniaFact]
    [Trait("Parity", "THR-1")]
    [Trait("Parity", "SCN-S1")]
    public void The_overview_refresh_blocks_input_and_the_worker_runs_off_the_ui_thread_with_input_live()
    {
        var gather = new ManualResetEventSlim(true);
        var work = new ManualResetEventSlim();
        bool? workerOnUiThread = null;
        using var app = ScannerApp.Start(
            gatherer: (g, h, l, p) =>
            {
                gather.Wait(TimeSpan.FromSeconds(30));
                return OverviewReport.Gather(g, h, l, p);
            },
            worker: s =>
            {
                workerOnUiThread = Dispatcher.UIThread.CheckAccess();
                work.Wait(TimeSpan.FromSeconds(30));
                return ScanWorker.Run(s);
            });

        gather.Reset();
        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        // The refresh's gathering is held, so input becoming blocked here is the refresh's doing; the wait is the
        // assertion (it times out otherwise).
        DispatcherWait.Until(() => app.Runtime.Input.IsBlocking);
        Assert.Equal("Refreshing Overview...", app.ScanningText.Text);
        gather.Set();

        DispatcherWait.Until(() => workerOnUiThread is not null);
        // The worker's phase is interactive: the tabs and panes take input while it runs.
        Assert.False(app.Runtime.Input.IsBlocking);
        Assert.Equal("Scanning...", (string?)app.Scanner.SidePane!.ScanButton.Content);
        work.Set();
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");

        Assert.False(workerOnUiThread);
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-8"), Deviation(20, "Each side-pane box change saves the scanner_* settings at once, off the UI thread in a blocking operation, instead of only when a scan starts.")]
    public void A_box_change_saves_at_once_off_the_ui_thread_in_a_blocking_operation()
    {
        // Bug #20, fixed (ADR-0006): the reference saved the boxes only in start_threaded_scan (and never read them
        // back). The port saves each change as it is made, through the same blocking write the scan's save used.
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var log = new HeldSaveLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        using var app = ScannerApp.Start(settingsStore: store);
        var before = File.ReadAllBytes(settingsPath);

        log.Hold();
        MouseClick.On(app.Scanner.SidePane!.Checks[ScanSetting.JunkFiles]);
        DispatcherWait.Until(() => log.WritesOnUiThread.Count == 1);

        Assert.False(log.WritesOnUiThread[0]);
        Assert.True(app.Runtime.Input.IsBlocking);
        Assert.Equal(before, File.ReadAllBytes(settingsPath));
        // The settings in effect change at the click, so a change made before the write lands builds on this one.
        Assert.False(store.Current.ScannerJunkFiles);

        log.Release();
        DispatcherWait.Until(() => !app.Runtime.Input.IsBlocking);
        Assert.Contains("\"scanner_JunkFiles\": false", File.ReadAllText(settingsPath), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-7"), Deviation(20, "Box changes save one after another, so the file ends as the last change left the boxes, and a scan saves nothing more.")]
    public void Box_changes_save_one_after_another_and_a_scan_saves_nothing_more()
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var log = new HeldSaveLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, log);
        using var app = ScannerApp.Start(settingsStore: store);
        var checks = app.Scanner.SidePane!.Checks;

        // The first write is held. A click while it is pending is discarded (ADR-0003: the pane froze with the Tk
        // thread), so a change can't overtake the save before it; SettingsStoreTests covers saves that overlap in code.
        log.Hold(1);
        MouseClick.On(checks[ScanSetting.JunkFiles]);
        DispatcherWait.Until(() => log.WritesOnUiThread.Count == 1);
        MouseClick.On(checks[ScanSetting.Errors]);
        Assert.True(checks[ScanSetting.Errors].IsChecked);
        log.Release();
        DispatcherWait.Until(() => !app.Runtime.Input.IsBlocking);

        ClickBoxAndWaitForTheSave(app, ScanSetting.Errors);
        ClickBoxAndWaitForTheSave(app, ScanSetting.JunkFiles);

        var text = File.ReadAllText(settingsPath);
        Assert.Contains("\"scanner_Errors\": false", text, StringComparison.Ordinal);
        Assert.Contains("\"scanner_JunkFiles\": true", text, StringComparison.Ordinal);
        Assert.False(store.Current.ScannerErrors);
        Assert.True(store.Current.ScannerJunkFiles);

        var saves = log.Entries.Count(e => e.Message == HeldSaveLogger.SavingMessage);
        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");
        Assert.Equal(saves, log.Entries.Count(e => e.Message == HeldSaveLogger.SavingMessage));
    }

    [AvaloniaFact]
    public async Task A_side_pane_box_click_during_a_blocking_operation_is_discarded_and_input_returns_when_it_ends()
    {
        // ADR-0003: the panes are owned windows, Toplevels the frozen Tk thread froze with the main window.
        using var app = ScannerApp.Start();
        var junk = app.Scanner.SidePane!.Checks[ScanSetting.JunkFiles];
        var gate = new TaskCompletionSource();

        var operation = app.Runtime.Operations.RunAsync("hold", OperationPhase.Blocking, () => gate.Task.Wait());
        MouseClick.On(junk);
        Assert.True(junk.IsChecked);

        gate.SetResult();
        await operation;
        MouseClick.On(junk);
        Assert.False(junk.IsChecked);
    }

    [AvaloniaFact]
    public async Task A_details_pane_button_click_during_a_blocking_operation_is_discarded_and_input_returns_when_it_ends()
    {
        using var app = ScannerApp.Start();
        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game" && app.Scanner.Tree!.Items.Count > 0);
        app.Scanner.Tree!.SelectedItem = app.Scanner.Tree.Items.OfType<TreeViewItem>().First().Items.OfType<TreeViewItem>().First();
        Dispatcher.UIThread.RunJobs();
        var copy = app.Scanner.DetailsPane!.Buttons.Single(b => (string?)b.Content == "Copy Details");
        var gate = new TaskCompletionSource();

        var operation = app.Runtime.Operations.RunAsync("hold", OperationPhase.Blocking, () => gate.Task.Wait());
        MouseClick.On(copy);
        Assert.Equal("Copy Details", (string?)copy.Content);

        gate.SetResult();
        await operation;
        MouseClick.On(copy);
        DispatcherWait.Until(() => (string?)copy.Content == CopyTextButton.CopiedText);
    }

    [AvaloniaFact]
    public async Task An_interactive_operation_leaves_the_panes_live()
    {
        using var app = ScannerApp.Start();
        var junk = app.Scanner.SidePane!.Checks[ScanSetting.JunkFiles];
        var gate = new TaskCompletionSource();

        var operation = app.Runtime.Operations.RunAsync("hold", OperationPhase.Interactive, () => gate.Task.Wait());
        MouseClick.On(junk);
        Assert.False(junk.IsChecked);

        gate.SetResult();
        await operation;
    }

    [AvaloniaFact]
    [Trait("Parity", "B-3")]
    [Trait("Parity", "THR-1")]
    [Deviation(22, "A worker that raises unexpectedly is reported and the scan ends: the seeded Overview problems are listed and Scan Game is enabled again.")]
    public void A_worker_that_raises_unexpectedly_is_reported_and_the_scan_ends_listing_the_overview_problems()
    {
        // B-3 (bug #22), fixed (ADR-0006): the reference's scan stayed stuck on Scanning... with Scan Game disabled. The
        // failures the worker expects (MO2's missing profile or modlist.txt) are problem rows now (ScanWorkerFailureTests);
        // anything else, such as a modlist.txt that isn't UTF-8, still raises.
        using var app = ScannerApp.Start(worker: _ => throw new System.Text.DecoderFallbackException("modlist.txt isn't UTF-8"));

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");

        var scan = app.Scanner.SidePane!.ScanButton;
        Assert.True(scan.IsEnabled);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100, app.Progress.Value);
        // Every box starts ticked, so the Overview's 16 problems (scanner/overview-issues) seeded the results; the
        // worker's own were lost with it.
        Assert.Equal("16 Results ~ Select an item for details", app.ResultsInfo.Text);
        Assert.Equal(["No Mod Manager", "Invalid Module", "File Not Found", "Invalid Archive"],
            app.Scanner.Tree!.Items.OfType<TreeViewItem>().Select(g => (string?)((TextBlock)g.Header!).Text));
        Assert.True(app.Scanner.IsSelectable);
        Assert.Contains(app.Logger.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Scanner scan", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-5")]
    [Trait("Parity", "THR-1")]
    public void Leaving_the_tab_mid_scan_makes_the_populate_fail_and_coming_back_gives_a_fresh_side_pane()
    {
        var work = new ManualResetEventSlim();
        var entered = false;
        using var app = ScannerApp.Start(worker: s =>
        {
            entered = true;
            work.Wait(TimeSpan.FromSeconds(30));
            return ScanWorker.Run(s);
        });

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => entered);
        app.Select(0);
        Assert.Null(app.Scanner.SidePane);
        Assert.Empty(app.Window.OwnedWindows);
        work.Set();
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);

        // populate_results raised ValueError before touching anything: no results, the label and progress as they were.
        Assert.Contains(app.Logger.Entries, e => e.Message.StartsWith("StdErr : " + ErrorReporter.UiThreadHeader, StringComparison.Ordinal));
        Assert.Empty(app.Scanner.Tree!.Items);
        Assert.Equal("", app.ResultsInfo.Text ?? "");
        Assert.True(app.ScanningText.IsVisible);
        Assert.Equal(100, app.Progress.Value);

        app.Select(1);
        var side = Assert.IsType<ScanSidePane>(app.Scanner.SidePane);
        Assert.Equal("Scan Game", (string?)side.ScanButton.Content);
        Assert.True(side.ScanButton.IsEnabled);
        // The new pane starts with the saved boxes (bug #20 fixed): every box ticked here, as the scan saved nothing.
        Assert.All(side.Checks.Values, c => Assert.True(c.IsChecked));
        Assert.Single(app.Window.OwnedWindows);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S1"), Deviation(22, "A scan whose Overview refresh dies ends with the error reported, nothing listed and Scan Game enabled again, instead of stuck on Refreshing Overview....")]
    public void A_refresh_that_dies_ends_the_scan_with_scan_game_enabled_again()
    {
        var fail = false;
        var workerRan = false;
        using var app = ScannerApp.Start(
            gatherer: (g, h, l, p) => fail ? throw new InvalidDataException("No [Archive] section") : OverviewReport.Gather(g, h, l, p),
            worker: s =>
            {
                workerRan = true;
                return ScanWorker.Run(s);
            });
        fail = true;

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null && !app.Runtime.Input.IsBlocking);
        Dispatcher.UIThread.RunJobs();

        // Bug #22's loose end, fixed (ADR-0006): the reference's start_threaded_scan died with the refresh, leaving
        // Scan Game disabled on Scanning... and the label on Refreshing Overview....
        AssertEndedBeforeTheWorker(app);
        Assert.Contains(app.Logger.Entries, e => e.Message.StartsWith("StdErr : " + ErrorReporter.OperationHeader("Overview refresh"), StringComparison.Ordinal));
        Assert.False(workerRan);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S1"), Deviation(22, "A scan whose Overview refresh raises past its own reporting has the error reported and ends with nothing listed and Scan Game enabled again.")]
    public void A_refresh_that_raises_is_reported_and_ends_the_scan_with_scan_game_enabled_again()
    {
        var workerRan = false;
        using var app = ScannerApp.Start(worker: s =>
        {
            workerRan = true;
            return ScanWorker.Run(s);
        });
        // The refresh itself raising, as rebuilding the Overview's frames could, rather than its gathering failing.
        app.Overview.Refresher = () => throw new InvalidOperationException("The frames couldn't be rebuilt.");

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);
        Dispatcher.UIThread.RunJobs();

        AssertEndedBeforeTheWorker(app);
        Assert.Contains(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal)
            && e.Message.Contains("The frames couldn't be rebuilt.", StringComparison.Ordinal));
        Assert.False(workerRan);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-2")]
    public void Collapse_all_and_expand_all_close_and_open_every_group()
    {
        using var app = ScannerApp.Start();
        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game" && app.Scanner.Tree!.Items.Count > 0);
        var groups = app.Scanner.Tree!.Items.OfType<TreeViewItem>().ToList();
        Assert.All(groups, g => Assert.True(g.IsExpanded));

        MouseClick.On(app.Named<Button>("CollapseAll"));
        Assert.All(groups, g => Assert.False(g.IsExpanded));

        MouseClick.On(app.Named<Button>("ExpandAll"));
        Assert.All(groups, g => Assert.True(g.IsExpanded));
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-3")]
    public void Scan_game_is_disabled_only_while_every_box_is_unticked()
    {
        using var app = ScannerApp.Start();
        var side = app.Scanner.SidePane!;

        foreach (var setting in ScanSettings.All)
        {
            Assert.True(side.ScanButton.IsEnabled);
            ClickBoxAndWaitForTheSave(app, setting);
        }

        Assert.False(side.ScanButton.IsEnabled);
        ClickBoxAndWaitForTheSave(app, ScanSetting.RaceSubgraphs);
        Assert.True(side.ScanButton.IsEnabled);
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-8"), Deviation(20, "The side pane's boxes start as the saved scanner_* settings say instead of all ticked.")]
    [Trait("Parity", "SCN-3")]
    public void The_side_pane_starts_with_the_saved_scanner_settings()
    {
        // Bug #20, fixed (ADR-0006): the reference ticked every box whatever settings.json said.
        var stored = CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub) with { ScannerErrors = false, ScannerJunkFiles = false };
        using var app = ScannerApp.Start(stored: stored);
        var side = app.Scanner.SidePane!;

        Assert.Equal(
            [true, false, true, true, false, true, true],
            ScanSettings.All.Select(s => side.Checks[s].IsChecked == true));
        Assert.True(side.ScanButton.IsEnabled);
    }

    [AvaloniaFact]
    [Trait("Parity", "SET-8"), Deviation(20, "With every saved scanner_* setting false, every box starts unticked, so Scan Game starts disabled.")]
    [Trait("Parity", "SCN-3")]
    public void With_every_saved_box_unticked_scan_game_starts_disabled()
    {
        var stored = CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub) with
        {
            ScannerOverviewIssues = false,
            ScannerErrors = false,
            ScannerWrongFormat = false,
            ScannerLoosePrevis = false,
            ScannerJunkFiles = false,
            ScannerProblemOverrides = false,
            ScannerRaceSubgraphs = false,
        };
        using var app = ScannerApp.Start(stored: stored);
        var side = app.Scanner.SidePane!;

        Assert.All(side.Checks.Values, c => Assert.False(c.IsChecked));
        Assert.False(side.ScanButton.IsEnabled);
        MouseClick.On(side.Checks[ScanSetting.OverviewIssues]);
        Assert.True(side.ScanButton.IsEnabled);
    }

    /// <summary>
    /// Clicks the side pane's box for <paramref name="setting"/>, then waits for the blocking save the change starts to
    /// land: a click while it is pending would be discarded (ADR-0003).
    /// </summary>
    private static void ClickBoxAndWaitForTheSave(ScannerApp app, ScanSetting setting)
    {
        MouseClick.On(app.Scanner.SidePane!.Checks[setting]);
        DispatcherWait.Until(() => !app.Runtime.Input.IsBlocking);
    }

    /// <summary>
    /// A scan that stopped before its worker has ended: Scan Game is back and enabled, the scanning label is hidden, and
    /// nothing is listed or selectable (the progress bar never moved).
    /// </summary>
    private static void AssertEndedBeforeTheWorker(ScannerApp app)
    {
        var scan = app.Scanner.SidePane!.ScanButton;
        Assert.Equal("Scan Game", (string?)scan.Content);
        Assert.True(scan.IsEnabled);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(0, app.Progress.Value);
        Assert.Empty(app.Scanner.Tree!.Items);
        Assert.Equal("", app.ResultsInfo.Text ?? "");
        Assert.False(app.Scanner.IsSelectable);
    }

    /// <summary>The Overview and Scanner tabs of the <c>scanner/overview-issues</c> machine in a shown main window, on the Scanner.</summary>
    private sealed class ScannerApp : IDisposable
    {
        private readonly MaterializedScenario _machine;
        private readonly TabControl _tabs;

        private ScannerApp(MaterializedScenario machine, MainWindow window, AppRuntime runtime, CapturingLogger logger, ScannerTab scanner, OverviewState overview)
        {
            Overview = overview;
            _machine = machine;
            Window = window;
            Runtime = runtime;
            Logger = logger;
            Scanner = scanner;
            _tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        }

        public MainWindow Window { get; }

        public AppRuntime Runtime { get; }

        public CapturingLogger Logger { get; }

        public ScannerTab Scanner { get; }

        /// <summary>The Overview state the scan refreshes and seeds from.</summary>
        public OverviewState Overview { get; }

        public TextBlock ScanningText => Named<TextBlock>("ScanningText");

        public TextBlock ResultsInfo => Named<TextBlock>("ResultsInfo");

        public ProgressBar Progress => Named<ProgressBar>("Progress");

        public static ScannerApp Start(
            Func<GameInstall, IHostEnvironment, ILogger, OverviewReport?, OverviewReport>? gatherer = null,
            Func<ScanSettings, ScanOutcome>? worker = null,
            CMToolkit.Core.Settings.AppSettings? stored = null,
            SettingsStore? settingsStore = null)
        {
            var machine = ParityScenarios.All().Single(s => s.Id == "scanner/overview-issues").Materialize();
            var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
                .GetAwaiter().GetResult()!;
            var logger = new CapturingLogger();
            var runtime = new AppRuntime(logger);
            var state = new OverviewState();
            var overview = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state)
            {
                OpenFolder = _ => { },
                Gatherer = gatherer ?? OverviewReport.Gather,
            };
            var settings = settingsStore
                ?? SettingsStore.Unsaved(stored ?? CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            var scanner = new ScannerTab(runtime, game, state, settings) { Worker = worker ?? ScanWorker.Run };
            var window = new MainWindow(runtime, [overview, scanner]);
            window.Show();
            DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
            var app = new ScannerApp(machine, window, runtime, logger, scanner, state);
            app.Select(1);
            return app;
        }

        public void Select(int index)
        {
            _tabs.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
        }

        public T Named<T>(string name)
            where T : Control
            => Scanner.Tree!.FindLogicalAncestorOfType<Grid>()!.GetLogicalDescendants().OfType<T>().First(c => c.Name == name);

        public void Dispose()
        {
            Window.Close();
            _machine.Dispose();
        }
    }
}
