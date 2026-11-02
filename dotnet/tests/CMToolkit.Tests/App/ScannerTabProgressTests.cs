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
/// The tab's side of the scan's progress, timed as the scenarios can't time it: the statuses the worker reports through
/// the sink the tab gives it reach the scanning label and the progress bar as they are, in order, while it runs; they
/// are drained before the results replace them, still land when the worker dies, and are dropped once the scan is over
/// (ADR-0003).
/// </summary>
public sealed class ScannerTabProgressTests
{
    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    public void Statuses_reach_the_label_and_the_bar_while_the_worker_runs()
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

        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => reported.IsSet && app.ScanningText.Text == "Scanning... 1/2: Meshes");
        Assert.Equal(50.0, app.Progress.Value);
        Assert.True(app.ScanningText.IsVisible);

        gate.Set();
        DispatcherWait.Until(() => scan.IsCompleted);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    public void Statuses_still_queued_when_the_worker_returns_are_applied_before_the_results()
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

        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => scan.IsCompleted);

        Assert.Equal(
            [
                (new ScanStatus("Building mod file index...", 1), "Building mod file index...", 1.0),
                (new ScanStatus("Scanning... 0/1: Meshes", 0), "Scanning... 0/1: Meshes", 0.0),
            ],
            applied);
        Assert.Equal(100.0, app.Progress.Value);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    [Trait("Parity", "B-3")]
    [Deviation(22, "A worker that dies unexpectedly has its queued updates applied, and then the scan ends listing the seeded Overview problems only instead of staying stuck.")]
    public void A_worker_that_dies_has_its_updates_applied_and_then_the_scan_ends()
    {
        var applied = new List<string>();
        using var app = ProgressApp.Start((s, progress) =>
        {
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            throw new IOException("worker died");
        });
        app.Scanner.ProgressApplied += (_, _) => applied.Add(app.ScanningText.Text!);

        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => scan.IsCompleted);

        // Bug #22, fixed (ADR-0006): the reference left the last update shown with Scan Game stuck on Scanning...
        Assert.Equal("Scanning... 1/2: Meshes", applied[^1]);
        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
        Assert.Equal("Scan Game", (string?)app.Scanner.SidePane!.ScanButton.Content);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    public void A_status_after_the_scan_is_over_changes_nothing()
    {
        // The sink the tab gave the scan, kept past its end, as a worker thread still running might keep it.
        IProgress<ScanStatus>? given = null;
        using var app = ProgressApp.Start((s, progress) =>
        {
            given = progress;
            return ScanOutcome.Empty;
        });
        var applied = 0;
        app.Scanner.ProgressApplied += (_, _) => applied++;
        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => scan.IsCompleted);

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

        private ProgressApp(MaterializedScenario machine, MainWindow window, ScannerTab scanner)
        {
            _machine = machine;
            Window = window;
            Scanner = scanner;
        }

        public MainWindow Window { get; }

        public ScannerTab Scanner { get; }

        public TextBlock ScanningText => Named<TextBlock>("ScanningText");

        public ProgressBar Progress => Named<ProgressBar>("Progress");

        /// <summary>Starts the app with <paramref name="worker"/> as the scan, given the snapshot and the tab's progress sink.</summary>
        public static ProgressApp Start(Func<ScanSettings, IProgress<ScanStatus>, ScanOutcome> worker)
        {
            var machine = ParityScenarios.All().Single(s => s.Id == "scanner/overview-issues").Materialize();
            var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
                .GetAwaiter().GetResult()!;
            var runtime = new AppRuntime(new CapturingLogger());
            var state = new OverviewState(runtime, p => CMToolkit.Core.Overview.OverviewReport.Gather(game, machine.Host, runtime.Logger, p));
            var settings = SettingsStore.Unsaved(CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            var scanner = new ScannerTab(runtime, game, state, settings) { Host = machine.Host, Worker = worker };
            // The Scanner needs only the loaded module, not an Overview tab; the first tab is a placeholder.
            var window = new MainWindow(runtime, [new ProbeTab("Other", "OtherTab"), scanner]);
            window.Show();
            var load = state.LoadAsync();
            DispatcherWait.Until(() => load.IsCompleted && !runtime.Input.IsBlocking);
            load.GetAwaiter().GetResult();
            window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            return new ProgressApp(machine, window, scanner);
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
