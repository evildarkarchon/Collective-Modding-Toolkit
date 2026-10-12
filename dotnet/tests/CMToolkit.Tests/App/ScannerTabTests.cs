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
        bool? blockedDuringRefresh = null;
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
        DispatcherWait.Until(() => app.Runtime.Input.IsBlocking);
        blockedDuringRefresh = app.Runtime.Input.IsBlocking;
        Assert.Equal("Refreshing Overview...", app.ScanningText.Text);
        gather.Set();

        DispatcherWait.Until(() => workerOnUiThread is not null);
        // The worker's phase is interactive: the tabs and panes take input while it runs.
        Assert.False(app.Runtime.Input.IsBlocking);
        Assert.Equal("Scanning...", (string?)app.Scanner.SidePane!.ScanButton.Content);
        work.Set();
        DispatcherWait.Until(() => (string?)app.Scanner.SidePane!.ScanButton.Content == "Scan Game");

        Assert.True(blockedDuringRefresh);
        Assert.False(workerOnUiThread);
    }

    [AvaloniaFact]
    [Trait("Parity", "B-3")]
    [Trait("Parity", "THR-1")]
    public void A_worker_that_raises_is_reported_and_leaves_the_scan_stuck_on_scanning()
    {
        // B-3 (bug #22): reproduced, not fixed. The fix lists the results it has, or at least re-enables Scan Game.
        using var app = ScannerApp.Start(worker: _ => throw new FileNotFoundException("File doesn't exist: modlist.txt"));

        MouseClick.On(app.Scanner.SidePane!.ScanButton);
        DispatcherWait.Until(() => app.Runtime.Errors.Window is not null);
        Dispatcher.UIThread.RunJobs();

        var scan = app.Scanner.SidePane!.ScanButton;
        Assert.Equal("Scanning...", (string?)scan.Content);
        Assert.False(scan.IsEnabled);
        Assert.True(app.ScanningText.IsVisible);
        // Every box starts ticked, so a data walk was due.
        Assert.Equal("Building mod file index...", app.ScanningText.Text);
        Assert.Equal(1, app.Progress.Value);
        Assert.Empty(app.Scanner.Tree!.Items);
        Assert.False(app.Scanner.IsSelectable);
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
        // The new pane starts all ticked again (B-1, bug #20).
        Assert.All(side.Checks.Values, c => Assert.True(c.IsChecked));
        Assert.Single(app.Window.OwnedWindows);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S1")]
    public void A_refresh_that_dies_stops_the_scan_on_refreshing_overview()
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

        Assert.Equal("Scanning...", (string?)app.Scanner.SidePane!.ScanButton.Content);
        Assert.False(app.Scanner.SidePane.ScanButton.IsEnabled);
        Assert.Equal("Refreshing Overview...", app.ScanningText.Text);
        Assert.Equal(0, app.Progress.Value);
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
            MouseClick.On(side.Checks[setting]);
        }

        Assert.False(side.ScanButton.IsEnabled);
        MouseClick.On(side.Checks[ScanSetting.RaceSubgraphs]);
        Assert.True(side.ScanButton.IsEnabled);
    }

    /// <summary>The Overview and Scanner tabs of the <c>scanner/overview-issues</c> machine in a shown main window, on the Scanner.</summary>
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

        public static ScannerApp Start(
            Func<GameInstall, IHostEnvironment, ILogger, OverviewReport?, OverviewReport>? gatherer = null,
            Func<ScanSettings, ScanOutcome>? worker = null)
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
            var settings = SettingsStore.Unsaved(CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            var scanner = new ScannerTab(runtime, game, state, settings) { Worker = worker ?? ScanWorker.Run };
            var window = new MainWindow(runtime, [overview, scanner]);
            window.Show();
            DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
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
