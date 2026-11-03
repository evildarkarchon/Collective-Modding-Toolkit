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
/// What the <c>scanner-tab</c> scenarios can't record, drawn by the tab from a real <see cref="ScanState"/>: what a scan
/// that stops early lists and when, what a refresh that dies leaves, leaving the tab mid-scan and coming back, the panes
/// during a blocking Operation, the box saves, Scan Game's rule and Collapse All / Expand All. The Scan's lifecycle is
/// <see cref="ScanStateTests"/>'.
/// </summary>
public sealed class ScannerTabTests
{
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
        // Built as ScanChecks.Run builds a stopped-early outcome: the seed (the snapshot's Overview problems, since
        // Overview Issues is ticked), then what the checks found, then the stopped-early row. The status is the last one
        // the scan reported before it stopped.
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
    public void Leaving_the_tab_mid_scan_opens_no_error_window_and_coming_back_shows_its_latest_status_then_its_results()
    {
        using var away = new ManualResetEventSlim();
        using var finish = new ManualResetEventSlim();
        var entered = false;
        var reportedAway = false;
        using var app = ScannerApp.Start(runScan: (s, progress) =>
        {
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            Volatile.Write(ref entered, true);
            away.Wait(TaskTimeouts.Default);
            // Reported while the user is on another tab: the label and the bar still take it.
            progress.Report(new ScanStatus("Scanning... 2/2: Textures", 75));
            Volatile.Write(ref reportedAway, true);
            finish.Wait(TaskTimeouts.Default);
            return ScanChecks.Run(s);
        });

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => Volatile.Read(ref entered));
        app.Select(0);
        Assert.Null(app.Scanner.SidePane);
        Assert.Empty(app.Window.OwnedWindows);
        away.Set();
        DispatcherWait.Until(() => Volatile.Read(ref reportedAway));

        // Back mid-scan: the label and the bar show where the scan has got to, and a fresh side pane opens.
        app.Select(1);
        Assert.True(app.ScanningText.IsVisible);
        Assert.Equal("Scanning... 2/2: Textures", app.ScanningText.Text);
        Assert.Equal(75, app.Progress.Value);
        Assert.Empty(app.Scanner.Tree!.Items);
        Assert.Single(app.Window.OwnedWindows);

        // Away again when the scan ends: its results are listed all the same.
        app.Select(0);
        finish.Set();
        DispatcherWait.Until(() => !app.Scan.IsRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(app.Runtime.Errors.Window);
        Assert.DoesNotContain(app.Logger.Entries, e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal));

        app.Select(1);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100, app.Progress.Value);
        Assert.NotEmpty(app.Scanner.Tree!.Items);
        Assert.EndsWith(" Results ~ Select an item for details", app.ResultsInfo.Text, StringComparison.Ordinal);
        Assert.True(app.Scanner.IsSelectable);
        var side = Assert.IsType<ScanSidePane>(app.Scanner.SidePane);
        Assert.Equal("Scan Game", (string?)side.ScanButton.Content);
        Assert.True(side.ScanButton.IsEnabled);
        // The new pane starts with the saved boxes (bug #20 fixed): every box ticked here, as the scan saved nothing.
        Assert.All(side.Checks.Values, c => Assert.True(c.IsChecked));
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S1"), Deviation(22, "A scan whose Overview refresh dies ends with the error reported, nothing listed and Scan Game enabled again, instead of stuck on Refreshing Overview....")]
    public void A_refresh_that_dies_ends_the_scan_with_scan_game_enabled_again()
    {
        var fail = false;
        using var app = ScannerApp.Start(
            gatherer: real => p => fail ? throw new InvalidDataException("No [Archive] section") : real(p));
        fail = true;

        // Every text Scan Game shows from the click on. What reaches the Error Window when the refresh dies is
        // ScanStateTests'; the tab is read once it has drawn the Scan's start and then its end, since the refresh can
        // die before the click's own jobs have run out.
        var button = app.Scanner.SidePane!.ScanButton;
        var drawn = new List<string?>();
        button.PropertyChanged += (_, e) =>
        {
            if (e.Property == ContentControl.ContentProperty)
            {
                drawn.Add((string?)e.NewValue);
            }
        };

        MouseClick.On(button);
        DispatcherWait.Until(() => drawn is [.., "Scan Game"] && !app.Runtime.Input.IsBlocking);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["Scanning...", "Scan Game"], drawn);

        // Bug #22's loose end, fixed (ADR-0006): the reference's start_threaded_scan died with the refresh, leaving
        // Scan Game disabled on Scanning... and the label on Refreshing Overview....
        AssertEndedBeforeTheWorker(app);
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
                return ScanChecks.Run(s);
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

    [AvaloniaFact]
    public void Ticking_or_unticking_a_box_mid_scan_leaves_scan_game_disabled_on_scanning_and_saves_the_change()
    {
        using var held = new HeldScan();
        var store = SettingsStore.Unsaved(CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
        using var app = ScannerApp.Start(runScan: held.Run, settingsStore: store);
        var side = app.Scanner.SidePane!;
        MouseClick.On(side.ScanButton);
        held.WaitUntilEntered();

        // The scan's checks run as an Interactive Operation, so the boxes stay live while it runs.
        ClickBoxAndWaitForTheSave(app, ScanSetting.JunkFiles);
        Assert.False(side.Checks[ScanSetting.JunkFiles].IsChecked);
        AssertScanning(side);
        Assert.False(store.Current.ScannerJunkFiles);

        ClickBoxAndWaitForTheSave(app, ScanSetting.JunkFiles);
        Assert.True(side.Checks[ScanSetting.JunkFiles].IsChecked);
        AssertScanning(side);
        Assert.True(store.Current.ScannerJunkFiles);

        held.Release();
        DispatcherWait.Until(() => !app.Scan.IsRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Scan Game", (string?)side.ScanButton.Content);
        Assert.True(side.ScanButton.IsEnabled);
    }

    [AvaloniaFact]
    public void A_side_pane_opened_mid_scan_shows_scanning_with_scan_game_disabled_until_the_scan_ends()
    {
        using var held = new HeldScan();
        using var app = ScannerApp.Start(runScan: held.Run);
        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        held.WaitUntilEntered();

        app.Select(0);
        app.Select(1);
        var side = Assert.IsType<ScanSidePane>(app.Scanner.SidePane);
        AssertScanning(side);

        // The scan ends in this pane rather than the one it started from, which is closed.
        held.Release();
        DispatcherWait.Until(() => !app.Scan.IsRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Scan Game", (string?)side.ScanButton.Content);
        Assert.True(side.ScanButton.IsEnabled);
    }

    [AvaloniaFact]
    public void A_scan_that_ends_with_every_box_unticked_leaves_scan_game_disabled()
    {
        using var held = new HeldScan();
        using var app = ScannerApp.Start(runScan: held.Run);
        var side = app.Scanner.SidePane!;
        MouseClick.On(side.ScanButton);
        held.WaitUntilEntered();

        foreach (var setting in ScanSettings.All)
        {
            ClickBoxAndWaitForTheSave(app, setting);
            AssertScanning(side);
        }

        held.Release();
        DispatcherWait.Until(() => !app.Scan.IsRunning);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Scan Game", (string?)side.ScanButton.Content);
        Assert.False(side.ScanButton.IsEnabled);
        // The scan ran with the boxes it started from, so it still listed what they asked for.
        Assert.NotEmpty(app.Scanner.Tree!.Items);
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

    /// <summary>Scan Game as it is while a Scan runs: disabled, showing <c>Scanning...</c>.</summary>
    private static void AssertScanning(ScanSidePane side)
    {
        Assert.Equal("Scanning...", (string?)side.ScanButton.Content);
        Assert.False(side.ScanButton.IsEnabled);
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
    /// A scripted <see cref="ScanState.RunScan"/> that holds mid-run until released, then runs the real scan over the
    /// snapshot it was given.
    /// </summary>
    private sealed class HeldScan : IDisposable
    {
        private readonly ManualResetEventSlim _finish = new();
        private bool _entered;

        /// <summary>The scan: marks itself entered, waits for <see cref="Release"/>, then scans.</summary>
        public ScanOutcome Run(ScanSnapshot snapshot, IProgress<ScanStatus> progress)
        {
            Volatile.Write(ref _entered, true);
            _finish.Wait(TaskTimeouts.Default);
            return CMToolkit.Core.Scanner.ScanChecks.Run(snapshot);
        }

        /// <summary>Runs dispatcher jobs until the scan has started its checks, past the Overview refresh.</summary>
        public void WaitUntilEntered() => DispatcherWait.Until(() => Volatile.Read(ref _entered));

        /// <summary>Lets the scan finish.</summary>
        public void Release() => _finish.Set();

        /// <summary>Releases the scan, so a failed test doesn't leave a worker waiting.</summary>
        /// <remarks>
        /// The event is set, not disposed: a worker still inside <see cref="ManualResetEventSlim.Wait(TimeSpan)"/> would
        /// throw on a disposed one, and a slim event that never handed out its wait handle holds nothing to free.
        /// </remarks>
        public void Dispose() => _finish.Set();
    }

    /// <summary>
    /// The Scanner tab of the <c>scanner/overview-issues</c> machine, over a loaded Overview Report module, after another
    /// tab in a shown main window, on the Scanner.
    /// </summary>
    private sealed class ScannerApp : IDisposable
    {
        private readonly MaterializedScenario _machine;
        private readonly TabControl _tabs;

        private ScannerApp(MaterializedScenario machine, MainWindow window, AppRuntime runtime, CapturingLogger logger, ScanState scan, ScannerTab scanner)
        {
            _machine = machine;
            Window = window;
            Runtime = runtime;
            Logger = logger;
            Scan = scan;
            Scanner = scanner;
            _tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
        }

        public MainWindow Window { get; }

        public AppRuntime Runtime { get; }

        public CapturingLogger Logger { get; }

        /// <summary>The Scan module the tab draws.</summary>
        public ScanState Scan { get; }

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
        /// The scan after its Overview refresh (<see cref="ScanState.RunScan"/>); the real one by default.
        /// </param>
        /// <param name="stored">The settings the side pane starts from, unsaved; the defaults by default.</param>
        /// <param name="settingsStore">The settings store, in place of <paramref name="stored"/>.</param>
        /// <param name="overviewTab">
        /// Whether the first tab is a real Overview tab over the module, which then loads it. A placeholder tab otherwise,
        /// and the module is loaded directly: the Scanner needs only the module.
        /// </param>
        public static ScannerApp Start(
            Func<Func<OverviewReport?, OverviewReport>, Func<OverviewReport?, OverviewReport>>? gatherer = null,
            Func<ScanSnapshot, IProgress<ScanStatus>, ScanOutcome>? runScan = null,
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
            var scan = new ScanState(runtime, game, state, machine.Host) { RunScan = runScan ?? ((s, _) => CMToolkit.Core.Scanner.ScanChecks.Run(s)) };
            var scanner = new ScannerTab(runtime, game, scan, settings) { Host = machine.Host };
            var window = new MainWindow(runtime, [first, scanner]);
            window.Show();
            var load = overviewTab ? null : state.LoadAsync();
            DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
            load?.GetAwaiter().GetResult();
            var app = new ScannerApp(machine, window, runtime, logger, scan, scanner);
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
