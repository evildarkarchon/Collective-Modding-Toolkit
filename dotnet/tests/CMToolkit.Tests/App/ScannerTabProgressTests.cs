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
/// The tab's side of the SCN-S4 queue protocol, timed as the scenarios can't time it (the reference's worker never sent
/// the folder tuple, bug #89, which the port fixes): updates the worker reports reach the scanning label and the
/// progress bar in order while it runs, are drained before the results replace them, still land when the worker dies
/// (B-3), and are dropped once the scan is over (no B-2 race: ADR-0003).
/// </summary>
public sealed class ScannerTabProgressTests
{
    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    public void Updates_reach_the_label_and_the_bar_while_the_worker_runs()
    {
        var gate = new ManualResetEventSlim();
        var reported = new ManualResetEventSlim();
        using var app = ProgressApp.Start(progress => s =>
        {
            progress().Report(new ScanUpdate.Folder(ScanWorker.RaceSubgraphStep));
            progress().Report(new ScanUpdate.TopLevelFolders(["F4SE", "Meshes"]));
            progress().Report(new ScanUpdate.Folder("Meshes"));
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
    public void Updates_still_queued_when_the_worker_returns_are_applied_before_the_results()
    {
        var applied = new List<ScanUpdate>();
        using var app = ProgressApp.Start(progress => s =>
        {
            progress().Report(new ScanUpdate.TopLevelFolders(["Meshes"]));
            progress().Report(new ScanUpdate.Folder("Meshes"));
            return ScanOutcome.Empty;
        });
        app.Scanner.ProgressApplied += (_, update) => applied.Add(update);

        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => scan.IsCompleted);

        Assert.Equal([new ScanUpdate.TopLevelFolders(["Meshes"]), new ScanUpdate.Folder("Meshes")], applied, UpdateComparer.Instance);
        Assert.Equal(100.0, app.Progress.Value);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-S4")]
    [Trait("Parity", "B-3")]
    [Deviation(22, "A worker that dies has its queued updates applied, and then the scan ends listing the seeded Overview problems only instead of staying stuck.")]
    public void A_worker_that_dies_has_its_updates_applied_and_then_the_scan_ends()
    {
        var applied = new List<string>();
        using var app = ProgressApp.Start(progress => s =>
        {
            progress().Report(new ScanUpdate.TopLevelFolders(["F4SE", "Meshes"]));
            progress().Report(new ScanUpdate.Folder("Meshes"));
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
    public void An_update_after_the_scan_is_over_changes_nothing()
    {
        using var app = ProgressApp.Start(progress => s => ScanOutcome.Empty);
        var scan = app.Scanner.ScanAsync();
        DispatcherWait.Until(() => scan.IsCompleted);

        app.Scanner.Progress.Report(new ScanUpdate.TopLevelFolders(["Meshes"]));
        app.Scanner.Progress.Report(new ScanUpdate.Folder("Meshes"));
        Dispatcher.UIThread.RunJobs();

        Assert.False(app.ScanningText.IsVisible);
        Assert.Equal(100.0, app.Progress.Value);
    }

    private sealed class UpdateComparer : IEqualityComparer<ScanUpdate>
    {
        public static readonly UpdateComparer Instance = new();

        public bool Equals(ScanUpdate? x, ScanUpdate? y) => (x, y) switch
        {
            (ScanUpdate.TopLevelFolders a, ScanUpdate.TopLevelFolders b) => a.Names.SequenceEqual(b.Names),
            _ => object.Equals(x, y),
        };

        public int GetHashCode(ScanUpdate obj) => 0;
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

        /// <summary>Starts the app with the worker <paramref name="worker"/> makes from a getter for the tab's progress.</summary>
        public static ProgressApp Start(Func<Func<IProgress<ScanUpdate>>, Func<ScanSettings, ScanOutcome>> worker)
        {
            var machine = ParityScenarios.All().Single(s => s.Id == "scanner/overview-issues").Materialize();
            var game = Task.Run(() => GameInstallLocator.LocateAsync(machine.Host, new ScriptedStartupDialogs([], machine.Host.Expand)))
                .GetAwaiter().GetResult()!;
            var runtime = new AppRuntime(new CapturingLogger());
            var state = new OverviewState();
            var overview = new OverviewTab(runtime, machine.Host, PcInfo.Read(machine.Host, new CapturingLogger()), game, state) { OpenFolder = _ => { } };
            var settings = SettingsStore.Unsaved(CMToolkit.Core.Settings.AppSettings.Defaults(DownloadSource.GitHub));
            ScannerTab? scanner = null;
            scanner = new ScannerTab(runtime, game, state, settings) { Host = machine.Host, Worker = worker(() => scanner!.Progress) };
            var window = new MainWindow(runtime, [overview, scanner]);
            window.Show();
            DispatcherWait.Until(() => state.Report is not null && !runtime.Input.IsBlocking);
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
