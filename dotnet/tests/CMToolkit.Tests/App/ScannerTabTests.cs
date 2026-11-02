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
using CMToolkit.Core.Problems;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// What the <c>scanner-tab</c> scenarios can't record: the scan's two phases (a blocking Overview refresh, then the rest
/// of the scan as an interactive operation), a scan that stops early or throws, a refresh that dies, leaving the tab
/// mid-scan (listing the results then fails because the side pane is gone), and Collapse All / Expand All.
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
            gatherer: real => p =>
            {
                gather.Wait(TimeSpan.FromSeconds(30));
                return real(p);
            },
            runScan: (s, _) =>
            {
                workerOnUiThread = Dispatcher.UIThread.CheckAccess();
                work.Wait(TimeSpan.FromSeconds(30));
                return Scan.Run(s);
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

        var operation = app.Runtime.Actions.Run("hold", OperationPhase.Blocking, () => gate.Task.Wait());
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

        var operation = app.Runtime.Actions.Run("hold", OperationPhase.Blocking, () => gate.Task.Wait());
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

        var operation = app.Runtime.Actions.Run("hold", OperationPhase.Interactive, () => gate.Task.Wait());
        MouseClick.On(junk);
        Assert.False(junk.IsChecked);

        gate.SetResult();
        await operation;
    }

    [AvaloniaFact]
    public void A_scan_that_stopped_early_applies_its_statuses_shows_its_error_then_lists_what_it_found_and_the_stopped_early_row()
    {
        var error = new System.Text.DecoderFallbackException("modlist.txt isn't UTF-8");
        var found = Problem.Simple("101 SADD Records from 1 modules", ScanProblemTypes.RaceSubgraphRecordCount, "Too many.", "Merge some.");
        var stopping = new ScanStatus("Building mod file index...", 1);
        // Built as Scan.Run builds a stopped-early outcome: the seed (the snapshot's Overview problems, since Overview
        // Issues is ticked), then what the checks found, then the stopped-early row. The status is the last one the
        // scan reported before it stopped.
        using var app = ScannerApp.Start(runScan: (s, progress) =>
        {
            progress.Report(stopping);
            return ScanOutcome.StoppedEarly(
                [.. ScanResults.Seed(s[ScanSetting.OverviewIssues], s.OverviewProblems), found], error);
        });
        // What the tree and the scanning label hold when the Error Window opens: the scan's statuses are applied
        // first, and the error must show before the list appears.
        int? listedWhenTheErrorShowed = null;
        string? labelWhenTheErrorShowed = null;
        using var opened = Window.WindowOpenedEvent.AddClassHandler<ErrorWindow>((_, _) =>
        {
            listedWhenTheErrorShowed ??= app.Scanner.Tree!.Items.Count;
            labelWhenTheErrorShowed ??= app.ScanningText.Text;
        });

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game" && app.Scanner.IsSelectable);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, listedWhenTheErrorShowed);
        Assert.Equal(stopping.Text, labelWhenTheErrorShowed);
        // Reported once, under the Scanner scan's header, as the error itself and not a wrapper around it.
        var header = ErrorReporter.OperationHeader("Scanner scan");
        var report = Assert.Single(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {header}\nSystem.Text.DecoderFallbackException: modlist.txt isn't UTF-8", report.Message, StringComparison.Ordinal);
        Assert.StartsWith($"{header}\nSystem.Text.DecoderFallbackException: modlist.txt isn't UTF-8", app.Runtime.Errors.Window!.Text, StringComparison.Ordinal);

        var scan = app.Scanner.SidePane!.ScanButton;
        Assert.True(scan.IsEnabled);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100, app.Progress.Value);
        // The tab lists the outcome as it is: the Overview's 16 problems (scanner/overview-issues), then the one problem
        // the checks found, then the stopped-early row. The count includes all of them.
        Assert.Equal("18 Results ~ Select an item for details", app.ResultsInfo.Text);
        var groups = app.Scanner.Tree!.Items.OfType<TreeViewItem>().ToList();
        Assert.Equal(
            ["No Mod Manager", "Invalid Module", "File Not Found", "Invalid Archive", "Race Subgraph Record Count", "Incomplete Scan"],
            groups.Select(g => ((TextBlock)g.Header!).Text));
        Assert.Equal(["101 SADD Records from 1 modules"], ItemTexts(groups[^2]));
        Assert.Equal(["Scan stopped early"], ItemTexts(groups[^1]));
    }

    [AvaloniaFact]
    public void An_exception_that_escapes_the_scan_is_reported_once_and_the_scan_ends_listing_the_stopped_early_row()
    {
        // The scan returns its own failures on its outcome (ScanFailureTests); this is the tab's last resort for an
        // exception that gets out anyway, so Scan Game can't stay disabled.
        using var app = ScannerApp.Start(runScan: (_, _) => throw new InvalidOperationException("escaped the scan"));

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");
        Dispatcher.UIThread.RunJobs();

        var scan = app.Scanner.SidePane!.ScanButton;
        Assert.True(scan.IsEnabled);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100, app.Progress.Value);
        // The scan seeds its own results, so everything it would have listed was lost with the exception. What's left is
        // the stopped-early row on its own, so the list can't pass for a clean scan; the count includes it.
        Assert.Equal("1 Results ~ Select an item for details", app.ResultsInfo.Text);
        var group = Assert.Single(app.Scanner.Tree!.Items.OfType<TreeViewItem>());
        Assert.Equal("Incomplete Scan", ((TextBlock)group.Header!).Text);
        Assert.Equal(["Scan stopped early"], ItemTexts(group));
        Assert.True(app.Scanner.IsSelectable);
        // The runner reported it; the tab doesn't report it again.
        var report = Assert.Single(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Error, report.Level);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Scanner scan")}\nSystem.InvalidOperationException: escaped the scan", report.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-5")]
    [Trait("Parity", "THR-1")]
    public void Leaving_the_tab_mid_scan_makes_the_populate_fail_and_coming_back_gives_a_fresh_side_pane()
    {
        var work = new ManualResetEventSlim();
        var entered = false;
        using var app = ScannerApp.Start(runScan: (s, _) =>
        {
            entered = true;
            work.Wait(TimeSpan.FromSeconds(30));
            return Scan.Run(s);
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
            gatherer: real => p => fail ? throw new InvalidDataException("No [Archive] section") : real(p),
            runScan: (s, _) =>
            {
                workerRan = true;
                return Scan.Run(s);
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

    /// <summary>
    /// The scan's refresh succeeds, but the Overview tab can't rebuild its frames from the new report. That rebuild is
    /// the Overview tab's own UI Action: its failure is reported, and the scan, which only needed the report, runs on.
    /// </summary>
    [AvaloniaFact]
    public void A_frame_rebuild_that_fails_in_the_overview_tab_is_reported_and_the_scan_still_runs()
    {
        var gathered = 0;
        var workerRan = false;
        using var app = ScannerApp.Start(
            gatherer: real => p =>
            {
                var report = real(p);
                // The frames are built from the binaries; the scan never reads them.
                return ++gathered == 1 ? report : report with { Binaries = null! };
            },
            runScan: (s, _) =>
            {
                workerRan = true;
                return Scan.Run(s);
            },
            overviewTab: true);

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => workerRan && (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");
        Dispatcher.UIThread.RunJobs();

        var report = Assert.Single(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.NullReferenceException", report.Message, StringComparison.Ordinal);
        Assert.Equal(2, gathered);
        Assert.True(app.Scanner.IsSelectable);
        Assert.NotEmpty(app.Scanner.Tree!.Items);
    }

    [AvaloniaFact]
    public void A_box_change_whose_save_fails_is_reported_once_by_the_runner_and_the_new_box_stays_in_effect()
    {
        // The write raises on its worker, past SettingsFile.Save's own logging, so the runner reports it under its
        // operation's name. The box change's UI Action must not report it a second time under the UI thread's header.
        using var temp = new TempDirectory();
        var log = ThrowingLogger.OnSave();
        var store = SettingsStore.Load(Path.Combine(temp.Path, "settings.json"), DownloadSource.GitHub, log);
        using var app = ScannerApp.Start(settingsStore: store);

        log.Arm();
        MouseClick.On(app.Scanner.SidePane!.Checks[ScanSetting.JunkFiles]);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null && !app.Runtime.Input.IsBlocking);
        Dispatcher.UIThread.RunJobs();

        var report = Assert.Single(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader(SettingsStore.SaveOperationName)}\n", report.Message);
        Assert.StartsWith(ErrorReporter.OperationHeader(SettingsStore.SaveOperationName), app.Runtime.Errors.Window!.Text);
        // The new box value stays in effect for the session either way.
        Assert.False(store.Current.ScannerJunkFiles);
    }

    [AvaloniaFact]
    public void A_scan_that_raises_is_reported_once_under_the_ui_thread_header()
    {
        // Leaving the tab mid-scan makes the populate raise (populate_results' ValueError): an exception no runner
        // reported, so the Scan Game UI Action reports it, once, under the UI thread's header.
        var work = new ManualResetEventSlim();
        var entered = false;
        using var app = ScannerApp.Start(runScan: (s, _) =>
        {
            entered = true;
            work.Wait(TimeSpan.FromSeconds(30));
            return Scan.Run(s);
        });

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => entered);
        app.Select(0);
        work.Set();
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);
        Dispatcher.UIThread.RunJobs();

        var report = Assert.Single(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));
        Assert.StartsWith(
            $"StdErr : {ErrorReporter.UiThreadHeader}\nSystem.InvalidOperationException: populate_results: the side pane is gone.",
            report.Message);
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

    /// <summary>The item rows' texts under <paramref name="group"/>, in tree order (rows without a mod column).</summary>
    private static IEnumerable<string?> ItemTexts(TreeViewItem group)
        => group.Items.OfType<TreeViewItem>().Select(i => ((TextBlock)i.Header!).Text);

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

    /// <summary>
    /// The Scanner tab of the <c>scanner/overview-issues</c> machine, over a loaded Overview Report module, after another
    /// tab in a shown main window, on the Scanner.
    /// </summary>
    private sealed class ScannerApp : IDisposable
    {
        private readonly MaterializedScenario _machine;
        private readonly TabControl _tabs;

        private ScannerApp(MaterializedScenario machine, MainWindow window, AppRuntime runtime, CapturingLogger logger, ScannerTab scanner)
        {
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

        public TextBlock ScanningText => Named<TextBlock>("ScanningText");

        public TextBlock ResultsInfo => Named<TextBlock>("ResultsInfo");

        public ProgressBar Progress => Named<ProgressBar>("Progress");

        /// <summary>
        /// Builds the module over the machine's real gathering (or <paramref name="gatherer"/> applied to it) and the
        /// Scanner over it, in a shown main window whose first tab is the one to leave the Scanner for, and selects the
        /// Scanner once the module is loaded.
        /// </summary>
        /// <param name="gatherer">Makes the module's gather function from the real one; null keeps the real one.</param>
        /// <param name="runScan">
        /// The scan after its Overview refresh (<see cref="ScannerTab.RunScan"/>); the real one by default.
        /// </param>
        /// <param name="stored">The settings the side pane starts from, unsaved; the defaults by default.</param>
        /// <param name="settingsStore">The settings store, in place of <paramref name="stored"/>.</param>
        /// <param name="overviewTab">
        /// Whether the first tab is a real Overview tab over the module, which then loads it. A placeholder tab otherwise,
        /// and the module is loaded directly: the Scanner needs only the module.
        /// </param>
        public static ScannerApp Start(
            Func<Func<OverviewReport?, OverviewReport>, Func<OverviewReport?, OverviewReport>>? gatherer = null,
            Func<ScanSettings, IProgress<ScanStatus>, ScanOutcome>? runScan = null,
            CMToolkit.Core.Settings.AppSettings? stored = null,
            SettingsStore? settingsStore = null,
            bool overviewTab = false)
        {
            var machine = ParityScenarios.All().Single(s => s.Id == "scanner/overview-issues").Materialize();
            var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
                .GetAwaiter().GetResult()!;
            var logger = new CapturingLogger();
            var runtime = new AppRuntime(logger);
            Func<OverviewReport?, OverviewReport> real = p => OverviewReport.Gather(game, machine.Host, logger, p);
            var state = new OverviewState(runtime, gatherer?.Invoke(real) ?? real);
            TabPage first = overviewTab
                ? new OverviewTab(runtime, PcInfo.Read(machine.Host, new CapturingLogger()), game, state) { OpenFolder = _ => { } }
                : new ProbeTab("Other", "OtherTab");
            var settings = settingsStore
                ?? SettingsStore.Unsaved(stored ?? CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            var scanner = new ScannerTab(runtime, game, state, settings) { Host = machine.Host, RunScan = runScan ?? ((s, _) => Scan.Run(s)) };
            var window = new MainWindow(runtime, [first, scanner]);
            window.Show();
            var load = overviewTab ? null : state.LoadAsync();
            DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
            load?.GetAwaiter().GetResult();
            var app = new ScannerApp(machine, window, runtime, logger, scanner);
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
