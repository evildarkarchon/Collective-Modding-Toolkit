using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Scan's lifecycle at its owner, <see cref="ScanState"/>, with no window: the events it raises and in what order,
/// whether a Scan is running, and what reaches the Error Window and the log. The Overview Report module gathers fixed
/// reports and the scan after its refresh is scripted, so nothing reads a machine.
/// </summary>
public sealed class ScanStateTests
{
    private static readonly Problem OverviewProblem =
        Problem.Simple("Fallout4.esm", ProblemTypes.InvalidModule, "Refreshed.", "None.");

    [AvaloniaFact]
    public void A_scan_raises_started_then_every_status_then_ended_listed_with_its_snapshot_and_outcome()
    {
        var outcome = new ScanOutcome([Problem.Simple("junk.txt", ScanProblemTypes.JunkFile, "Junk.", "Delete it.")], null);
        var fixture = Fixture.Start((_, progress) =>
        {
            progress.Report(new ScanStatus("Building mod file index...", 1));
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            return outcome;
        });
        var seen = fixture.Record();

        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));

        Assert.Equal(
            [
                "Started (running)",
                "Refreshing Overview... @ 0",
                "Refreshing Overview... @ 1",
                "Building mod file index... @ 1",
                "Scanning... 1/2: Meshes @ 50",
                "Scanning... 1/2: Meshes @ 100",
                "Ended Listed (not running)",
            ],
            seen.Lines);
        var listed = Assert.IsType<ScanEnded.Listed>(seen.Ended);
        Assert.Same(outcome, listed.Outcome);
        // The snapshot is taken after the refresh, so it carries the refreshed report's problems.
        Assert.Equal([OverviewProblem], listed.Snapshot.OverviewProblems);
        Assert.All(ScanSettings.All, s => Assert.True(listed.Snapshot[s]));
        Assert.False(fixture.Scan.IsRunning);
        Assert.Equal(2, fixture.Gathered);
    }

    [AvaloniaFact]
    public void A_refresh_that_dies_ends_the_scan_not_scanned_before_anything_is_checked()
    {
        var scanned = false;
        var calls = 0;
        var fixture = Fixture.Start(
            (s, _) =>
            {
                scanned = true;
                return ScanOutcome.Empty;
            },
            gather: _ => ++calls == 1
                ? new OverviewReport(InstallType.NextGen, [], null, [])
                : throw new InvalidDataException("No [Archive] section"));
        var seen = fixture.Record();

        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));

        // No status after the refresh: the label and the bar stay as the refresh left them until the tab hides them.
        Assert.Equal(["Started (running)", "Refreshing Overview... @ 0", "Ended NotScanned (not running)"], seen.Lines);
        Assert.False(scanned);
        Assert.False(fixture.Scan.IsRunning);
        // The runner reported it, once, under the refresh's own header.
        var report = Assert.Single(fixture.Reports);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Overview refresh")}\nSystem.IO.InvalidDataException: No [Archive] section", report.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_scan_that_stops_early_has_its_error_reported_once_before_it_ends_listing_its_outcome()
    {
        var error = new System.Text.DecoderFallbackException("modlist.txt isn't UTF-8");
        var stopped = ScanOutcome.StoppedEarly([Problem.Simple("junk.txt", ScanProblemTypes.JunkFile, "Junk.", "Delete it.")], error);
        var fixture = Fixture.Start((_, progress) =>
        {
            progress.Report(new ScanStatus("Building mod file index...", 1));
            return stopped;
        });
        var seen = fixture.Record();
        // What was in the Error Window and the log when each of the last two events was raised.
        var reportsAtTheLastStatus = -1;
        var reportsAtTheEnd = -1;
        fixture.Scan.StatusChanged += (_, status) =>
        {
            if (status.Progress == 100)
            {
                reportsAtTheLastStatus = fixture.Reports.Count;
            }
        };
        fixture.Scan.Ended += (_, _) => reportsAtTheEnd = fixture.Reports.Count;

        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));

        Assert.Equal(
            ["Started (running)", "Refreshing Overview... @ 0", "Refreshing Overview... @ 1", "Building mod file index... @ 1", "Building mod file index... @ 100", "Ended Listed (not running)"],
            seen.Lines);
        Assert.Same(stopped, Assert.IsType<ScanEnded.Listed>(seen.Ended).Outcome);
        Assert.Equal(1, reportsAtTheLastStatus);
        Assert.Equal(1, reportsAtTheEnd);
        // Reported once, under the Scanner scan's header, as the error itself and not a wrapper around it.
        var header = ErrorReporter.OperationHeader("Scanner scan");
        var report = Assert.Single(fixture.Reports);
        Assert.StartsWith($"StdErr : {header}\nSystem.Text.DecoderFallbackException: modlist.txt isn't UTF-8", report.Message, StringComparison.Ordinal);
        Assert.StartsWith($"{header}\nSystem.Text.DecoderFallbackException: modlist.txt isn't UTF-8", fixture.Runtime.Errors.Window!.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void An_exception_that_escapes_the_scan_is_reported_once_and_the_scan_ends_with_the_stopped_early_row_alone()
    {
        // The scan returns its own failures on its outcome (ScanChecksFailureTests); this is the last resort for an
        // exception that gets out anyway.
        var fixture = Fixture.Start((_, progress) =>
        {
            progress.Report(new ScanStatus("Scanning... 1/2: Meshes", 50));
            throw new InvalidOperationException("escaped the scan");
        });
        var seen = fixture.Record();

        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));

        Assert.Equal(
            ["Started (running)", "Refreshing Overview... @ 0", "Refreshing Overview... @ 1", "Scanning... 1/2: Meshes @ 50", "Scanning... 1/2: Meshes @ 100", "Ended Listed (not running)"],
            seen.Lines);
        // Everything the scan would have listed died with it (it seeds its own results), so the outcome is the
        // stopped-early row alone, carrying the error: an empty list would read as a clean scan.
        var outcome = Assert.IsType<ScanEnded.Listed>(seen.Ended).Outcome;
        Assert.Equal([ScanOutcome.StoppedEarlyRow], outcome.Problems);
        Assert.Equal("escaped the scan", outcome.Error?.Message);
        // The runner reported it; the module doesn't report it again.
        var report = Assert.Single(fixture.Reports);
        Assert.StartsWith($"StdErr : {ErrorReporter.OperationHeader("Scanner scan")}\nSystem.InvalidOperationException: escaped the scan", report.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_start_while_a_scan_runs_does_nothing_but_log_at_debug()
    {
        using var hold = new ManualResetEventSlim();
        var entered = 0;
        var fixture = Fixture.Start((_, _) =>
        {
            Interlocked.Increment(ref entered);
            hold.Wait(TaskTimeouts.Default);
            return ScanOutcome.Empty;
        });
        var first = fixture.Scan.StartAsync(Fixture.AllTicked);
        DispatcherWait.Until(() => Volatile.Read(ref entered) == 1);
        var seen = fixture.Record();
        var logged = fixture.Logger.Entries.Count;

        var second = fixture.Scan.StartAsync(Fixture.AllTicked);

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Empty(seen.Lines);
        Assert.True(fixture.Scan.IsRunning);
        Assert.Equal([new LogEntry(LogLevel.Debug, "Scanner : Scan : Already running, ignored")], fixture.Logger.Entries.Skip(logged));

        hold.Set();
        fixture.Await(first);
        Assert.Equal(1, entered);
        Assert.Equal("Ended Listed (not running)", seen.Lines[^1]);
        Assert.Equal(2, fixture.Gathered);
        Assert.Empty(fixture.Reports);
    }

    [AvaloniaFact]
    public void A_status_reported_after_the_scan_returned_is_dropped()
    {
        // The sink the module gave the scan, kept past its end, as a thread the scan left running might keep it.
        IProgress<ScanStatus>? given = null;
        var fixture = Fixture.Start((_, progress) =>
        {
            given = progress;
            return ScanOutcome.Empty;
        });
        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));
        var seen = fixture.Record();

        given!.Report(new ScanStatus("Scanning... 0/1: Meshes", 0));
        // And from a worker thread, which waits for the UI thread to handle it.
        var late = Task.Run(() => given.Report(new ScanStatus("Scanning... 1/1: Meshes", 100)));
        DispatcherWait.Until(() => late.IsCompleted);

        Assert.Empty(seen.Lines);
    }

    [AvaloniaFact]
    public void The_overview_refresh_blocks_input_and_the_scan_runs_off_the_ui_thread_with_input_live()
    {
        using var gather = new ManualResetEventSlim(true);
        using var work = new ManualResetEventSlim();
        bool? scanOnUiThread = null;
        var calls = 0;
        var fixture = Fixture.Start(
            (_, _) =>
            {
                scanOnUiThread = Dispatcher.UIThread.CheckAccess();
                work.Wait(TaskTimeouts.Default);
                return ScanOutcome.Empty;
            },
            gather: _ =>
            {
                if (++calls > 1)
                {
                    gather.Wait(TaskTimeouts.Default);
                }

                return new OverviewReport(InstallType.NextGen, [], null, []);
            });

        gather.Reset();
        var scan = fixture.Scan.StartAsync(Fixture.AllTicked);
        // The refresh's gathering is held, so input becoming blocked here is the refresh's doing; the wait is the
        // assertion (it times out otherwise).
        DispatcherWait.Until(() => fixture.Runtime.Input.IsBlocking);
        gather.Set();

        DispatcherWait.Until(() => scanOnUiThread is not null);
        // The scan's Operation is Interactive: the window takes input while it runs.
        Assert.False(fixture.Runtime.Input.IsBlocking);
        Assert.False(scanOnUiThread);
        work.Set();
        fixture.Await(scan);
    }

    [AvaloniaFact]
    public void A_start_off_the_ui_thread_throws_before_raising_anything_or_running_a_scan()
    {
        var scanned = false;
        var fixture = Fixture.Start((_, _) =>
        {
            scanned = true;
            return ScanOutcome.Empty;
        });
        var seen = fixture.Record();
        // Whether a Scan was running at any point while the start was in flight, sampled from the UI thread.
        var everRunning = false;

        var start = Task.Run(() => fixture.Scan.StartAsync(Fixture.AllTicked));
        DispatcherWait.Until(() =>
        {
            everRunning |= fixture.Scan.IsRunning;
            return start.IsCompleted;
        });

        Assert.IsType<InvalidOperationException>(start.Exception?.InnerException);
        // Every event is raised on the UI thread, so a start from elsewhere raises none of them.
        Assert.Empty(seen.Lines);
        Assert.False(everRunning);
        Assert.False(fixture.Scan.IsRunning);
        Assert.False(scanned);
        // Only the load gathered: the refresh never started.
        Assert.Equal(1, fixture.Gathered);
    }

    [AvaloniaFact]
    public void A_handler_that_throws_fails_the_start_and_leaves_no_scan_running()
    {
        var fixture = Fixture.Start((_, _) => ScanOutcome.Empty);
        var throwing = true;
        fixture.Scan.Started += (_, _) =>
        {
            if (throwing)
            {
                throw new InvalidOperationException("A reader broke.");
            }
        };
        var seen = fixture.Record();

        var failed = fixture.Scan.StartAsync(Fixture.AllTicked);
        DispatcherWait.Until(() => failed.IsCompleted);

        Assert.Equal("A reader broke.", failed.Exception?.InnerException?.Message);
        Assert.False(fixture.Scan.IsRunning);
        throwing = false;
        fixture.Await(fixture.Scan.StartAsync(Fixture.AllTicked));
        Assert.Equal("Ended Listed (not running)", seen.Lines[^1]);
    }

    /// <summary>
    /// A loaded Overview Report module over a fixed gathering (the load gathers an empty report, each refresh one with
    /// <see cref="OverviewProblem"/>) and a <see cref="ScanState"/> over it with a scripted scan, on a game that is
    /// never read.
    /// </summary>
    private sealed class Fixture
    {
        private int _gathered;

        private Fixture(Func<OverviewReport?, OverviewReport>? gather, Func<ScanSnapshot, IProgress<ScanStatus>, ScanOutcome> runScan)
        {
            Runtime = new AppRuntime(Logger);
            Overview = new OverviewState(Runtime, previous =>
            {
                var calls = Interlocked.Increment(ref _gathered);
                if (gather is not null)
                {
                    return gather(previous);
                }

                return calls == 1
                    ? new OverviewReport(InstallType.NextGen, [], null, [])
                    : new OverviewReport(InstallType.NextGen, [], null, [OverviewProblem]);
            });
            var empty = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            var game = new GameInstall(
                null, null, new GameDirectories(new PythonPath(@"C:\Nowhere\Fallout 4"), null, null), new GameInis(empty, empty, "en"));
            Scan = new ScanState(Runtime, game, Overview, new FakeHostEnvironment(new HostBlock(), @"C:\Nowhere")) { RunScan = runScan };
        }

        /// <summary>Every box ticked.</summary>
        public static IReadOnlyDictionary<ScanSetting, bool> AllTicked { get; } = ScanSettings.All.ToDictionary(s => s, _ => true);

        public CapturingLogger Logger { get; } = new();

        public AppRuntime Runtime { get; }

        public OverviewState Overview { get; }

        public ScanState Scan { get; }

        /// <summary>How many times the Overview Report has been gathered, the load included.</summary>
        public int Gathered => Volatile.Read(ref _gathered);

        /// <summary>The Error Window's reports in the log so far: the <c>StdErr : </c> lines.</summary>
        public IReadOnlyList<LogEntry> Reports => [.. Logger.Entries.Where(e => e.Message.StartsWith("StdErr : ", StringComparison.Ordinal))];

        /// <summary>Builds the fixture, with <paramref name="gather"/> in place of the fixed gathering if given, and loads the module.</summary>
        public static Fixture Start(
            Func<ScanSnapshot, IProgress<ScanStatus>, ScanOutcome> runScan, Func<OverviewReport?, OverviewReport>? gather = null)
        {
            var fixture = new Fixture(gather, runScan);
            fixture.Await(fixture.Overview.LoadAsync());
            return fixture;
        }

        /// <summary>Records every event <see cref="Scan"/> raises from now on, with whether a Scan was running at the time.</summary>
        public Recording Record()
        {
            var recording = new Recording();
            Scan.Started += (_, _) => recording.Lines.Add($"Started ({Running()})");
            Scan.StatusChanged += (_, status) => recording.Lines.Add($"{status.Text} @ {status.Progress}");
            Scan.Ended += (_, ended) =>
            {
                recording.Ended = ended;
                recording.Lines.Add($"Ended {ended.GetType().Name} ({Running()})");
            };
            return recording;

            string Running() => Scan.IsRunning ? "running" : "not running";
        }

        /// <summary>Runs the dispatcher until <paramref name="task"/> has finished, then rethrows its failure, if any.</summary>
        public void Await(Task task)
        {
            DispatcherWait.Until(() => task.IsCompleted);
            task.GetAwaiter().GetResult();
        }
    }

    /// <summary>What a <see cref="Fixture.Record"/> saw: one line per event, and the last <see cref="ScanEnded"/>.</summary>
    private sealed class Recording
    {
        public List<string> Lines { get; } = [];

        public ScanEnded? Ended { get; set; }
    }
}
