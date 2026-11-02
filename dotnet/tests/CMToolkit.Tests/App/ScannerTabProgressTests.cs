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
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// The tab's side of the scan's progress, timed as the scenarios can't time it: the statuses the scan reports through
/// the sink its <see cref="ScanState"/> gives it reach the scanning label and the progress bar as they are, in order,
/// with the refresh's statuses before them and the bar at 100 after them, while it runs; each
/// is applied before the scan goes on, so all of them are shown before the results replace them; they still land when
/// the scan throws, and are dropped once the scan is over (ADR-0003).
/// </summary>
public sealed class ScannerTabProgressTests
{
    [AvaloniaFact]
    public void Statuses_reach_the_label_and_the_bar_while_the_scan_runs()
    {
        var gate = new ManualResetEventSlim();
        var reported = new ManualResetEventSlim();
        using var app = ProgressApp.Start((s, progress) =>
        {
            progress.Report(new ScanStatus("Counting Race Subgraph records...", 1));
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            reported.Set();
            gate.Wait(TimeSpan.FromSeconds(30));
            return ScanOutcome.Empty;
        });

        var scan = app.StartScan();
        DispatcherWait.Until(() => reported.IsSet && app.ScanningText.Text == "Scanning... 1/2: Meshes");
        Assert.Equal(50.0, app.Progress.Value);
        Assert.True(app.ScanningText.IsVisible);

        gate.Set();
        DispatcherWait.Until(() => scan.IsCompleted);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
    }

    [AvaloniaFact]
    public void Each_status_is_applied_before_the_scan_goes_on()
    {
        // How many of the scan's own statuses the tab had applied each time Report handed control back to the scan (the
        // count less the refresh's statuses, applied before the scan began): the sink waits for the UI thread, so the
        // status just reported is always among them.
        var appliedWhenReportReturned = new List<int>();
        var applied = 0;
        using var app = ProgressApp.Start((s, progress) =>
        {
            var before = Volatile.Read(ref applied);
            progress.Report(new ScanStatus("Building mod file index...", 1));
            appliedWhenReportReturned.Add(Volatile.Read(ref applied) - before);
            progress.Report(new ScanStatus("Scanning... 0/1: Meshes", 0));
            appliedWhenReportReturned.Add(Volatile.Read(ref applied) - before);
            return ScanOutcome.Empty;
        });
        app.Scanner.ProgressApplied += (_, _) => Interlocked.Increment(ref applied);

        var scan = app.StartScan();
        DispatcherWait.Until(() => scan.IsCompleted);

        Assert.Equal([1, 2], appliedWhenReportReturned);
    }

    [AvaloniaFact]
    public void The_scans_last_status_is_the_last_one_applied_before_the_results_are_listed()
    {
        // What the tab showed, in order: each status as the label and bar applied it, then the moment the first result
        // group was listed.
        var shown = new List<string>();
        using var app = ProgressApp.Start((s, progress) =>
        {
            progress.Report(new ScanStatus("Building mod file index...", 1));
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            progress.Report(new ScanStatus("Scanning... 2/2: Textures", 100));
            // The seed alone: the snapshot's Overview problems, since Overview Issues is ticked.
            return new ScanOutcome([.. ScanResults.Seed(s[ScanSetting.OverviewIssues], s.OverviewProblems)], null);
        });
        app.Scanner.ProgressApplied += (_, status) => shown.Add($"{app.ScanningText.Text} @ {app.Progress.Value}");
        ((System.Collections.Specialized.INotifyCollectionChanged)app.Scanner.Tree!.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && !shown.Contains("listed"))
            {
                shown.Add("listed");
            }
        };

        var scan = app.StartScan();
        DispatcherWait.Until(() => scan.IsCompleted);

        // The refresh's two statuses come first, and the scan's end sets the bar to 100 under the scan's last label.
        Assert.Equal(
            [
                "Refreshing Overview... @ 0",
                "Refreshing Overview... @ 1",
                "Building mod file index... @ 1",
                "Scanning... 1/2: Meshes @ 50",
                "Scanning... 2/2: Textures @ 100",
                "Scanning... 2/2: Textures @ 100",
                "listed",
            ],
            shown);
        Assert.NotEmpty(app.Scanner.Tree!.Items);
    }

    [AvaloniaFact]
    public void Each_status_raises_the_notification_once_after_the_label_and_the_bar_show_it()
    {
        var applied = new List<(ScanStatus Status, string? Label, double Bar)>();
        using var app = ProgressApp.Start((s, progress) =>
        {
            progress.Report(new ScanStatus("Building mod file index...", 1));
            progress.Report(new ScanStatus("Scanning... 0/1: Meshes", 0));
            return ScanOutcome.Empty;
        });
        // The notification fires once per status, after the label and the bar show it.
        app.Scanner.ProgressApplied += (_, status) => applied.Add((status, app.ScanningText.Text, app.Progress.Value));

        var scan = app.StartScan();
        DispatcherWait.Until(() => scan.IsCompleted);

        Assert.Equal(
            [
                (new ScanStatus("Refreshing Overview...", 0), "Refreshing Overview...", 0.0),
                (new ScanStatus("Refreshing Overview...", 1), "Refreshing Overview...", 1.0),
                (new ScanStatus("Building mod file index...", 1), "Building mod file index...", 1.0),
                (new ScanStatus("Scanning... 0/1: Meshes", 0), "Scanning... 0/1: Meshes", 0.0),
                (new ScanStatus("Scanning... 0/1: Meshes", 100), "Scanning... 0/1: Meshes", 100.0),
            ],
            applied);
        Assert.Equal(100.0, app.Progress.Value);
    }

    [AvaloniaFact]
    public void A_scan_that_throws_has_its_statuses_applied_and_then_the_scan_ends()
    {
        var applied = new List<string>();
        using var app = ProgressApp.Start((s, progress) =>
        {
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            throw new IOException("worker died");
        });
        app.Scanner.ProgressApplied += (_, _) => applied.Add(app.ScanningText.Text!);

        var scan = app.StartScan();
        DispatcherWait.Until(() => scan.IsCompleted);

        // Bug #22, fixed (ADR-0006): the reference left the last update shown with Scan Game stuck on Scanning...
        Assert.Equal("Scanning... 1/2: Meshes", applied[^1]);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
        Assert.Equal("Scan Game", (string?)app.Scanner.SidePane!.ScanButton.Content);
    }

    [AvaloniaFact]
    public void A_status_after_the_scan_is_over_changes_nothing()
    {
        // The sink the scan was given, kept past its end, as a thread the scan left running might keep it.
        IProgress<ScanStatus>? given = null;
        using var app = ProgressApp.Start((s, progress) =>
        {
            given = progress;
            return ScanOutcome.Empty;
        });
        var scan = app.StartScan();
        DispatcherWait.Until(() => scan.IsCompleted);
        var applied = 0;
        app.Scanner.ProgressApplied += (_, _) => applied++;

        given!.Report(new ScanStatus("Scanning... 0/1: Meshes", 0));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, applied);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
    }

    /// <summary>The Scanner over the <c>scanner/overview-issues</c> machine with a scripted worker.</summary>
    private sealed class ProgressApp : IDisposable
    {
        private readonly MaterializedScenario _machine;

        private readonly ScanState _scan;

        private ProgressApp(MaterializedScenario machine, MainWindow window, ScanState scan, ScannerTab scanner)
        {
            _machine = machine;
            Window = window;
            _scan = scan;
            Scanner = scanner;
        }

        public MainWindow Window { get; }

        public ScannerTab Scanner { get; }

        public TextBlock ScanningText => Named<TextBlock>("ScanningText");

        public ProgressBar Progress => Named<ProgressBar>("Progress");

        /// <summary>Starts the app with <paramref name="runScan"/> as the scan, given the snapshot and the Scan's progress sink.</summary>
        public static ProgressApp Start(Func<ScanSettings, IProgress<ScanStatus>, ScanOutcome> runScan)
        {
            var machine = ParityScenarios.All().Single(s => s.Id == "scanner/overview-issues").Materialize();
            var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
                .GetAwaiter().GetResult()!;
            var runtime = new AppRuntime(new CapturingLogger());
            var state = new OverviewState(runtime, p => CMToolkit.Core.Overview.OverviewReport.Gather(game, machine.Host, runtime.Logger, p));
            var settings = SettingsStore.Unsaved(CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            var scan = new ScanState(runtime, game, state, machine.Host) { RunScan = runScan };
            var scanner = new ScannerTab(runtime, game, scan, settings) { Host = machine.Host };
            // The Scanner needs only the loaded module, not an Overview tab; the first tab is a placeholder.
            var window = new MainWindow(runtime, [new ProbeTab("Other", "OtherTab"), scanner]);
            window.Show();
            var load = state.LoadAsync();
            DispatcherWait.Until(() => load.IsCompleted && !runtime.Input.IsBlocking);
            load.GetAwaiter().GetResult();
            window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            return new ProgressApp(machine, window, scan, scanner);
        }

        /// <summary>Starts a Scan with the side pane's boxes, as Scan Game does, and returns the task that ends with it.</summary>
        public Task StartScan() => _scan.StartAsync(Scanner.SidePane!.Values);

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
