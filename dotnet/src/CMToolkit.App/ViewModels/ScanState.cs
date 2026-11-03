using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CMToolkit.Core.Scanner;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.ViewModels;

/// <summary>How a Scan ended: <see cref="NotScanned"/>, or <see cref="Listed"/> with what to list.</summary>
public abstract record ScanEnded
{
    private ScanEnded()
    {
    }

    /// <summary>The Overview refresh failed, already reported, so nothing was checked and there is nothing to list.</summary>
    public sealed record NotScanned : ScanEnded;

    /// <summary>
    /// The scan ran, and finished or stopped early; its outcome is what to list. An error on the outcome is already in the
    /// Error Window.
    /// </summary>
    /// <param name="Settings">The Scan's snapshot, which says whether rows get the mod column.</param>
    /// <param name="Outcome">What the scan found, in the order it is listed.</param>
    public sealed record Listed(ScanSettings Settings, ScanOutcome Outcome) : ScanEnded;
}

/// <summary>
/// The Scan module: it owns one Scan from Scan Game to the listed results, and whether one is running. The Scanner tab
/// starts a Scan through it and draws what its events say, wherever the user is when they are raised.
/// </summary>
/// <remarks>
/// A Scan runs in two Operations (ADR-0003): the Overview refresh, through <see cref="OverviewState.TryRefreshAsync"/>,
/// is Blocking; the scan after it, <see cref="RunScan"/>, is Interactive, so the window stays live and the user can
/// leave the Scanner tab without stopping it. Only one Scan runs at a time. Every event is raised on the UI thread, in
/// the order <see cref="Started"/>, <see cref="StatusChanged"/> for each status, then <see cref="Ended"/>. Start every
/// Scan on the UI thread.
/// </remarks>
public sealed class ScanState
{
    // The scan's Operation: the runner reports an escaped exception under its name, and a returned error is reported
    // under the same header, so both read alike in the Error Window.
    private const string ScanOperation = "Scanner scan";

    private readonly AppRuntime _runtime;
    private readonly GameInstall _game;
    private readonly OverviewState _overview;
    private readonly IHostEnvironment _host;

    // The last status raised: the next Scan's refresh status keeps its progress, and the final one keeps its text.
    private ScanStatus _last = new(string.Empty, 0);

    /// <summary>The module, with no Scan running.</summary>
    /// <param name="runtime">The runner the Scan's Operations run through, the Error Window and the log.</param>
    /// <param name="game">The installation the Scan checks.</param>
    /// <param name="overview">The Overview Report module: each Scan refreshes it first, and its problems seed the Scan.</param>
    /// <param name="host">
    /// The machine the Scan reads. Required, with no default, because the snapshot is taken with it: a module built
    /// without naming its machine would silently scan the real one.
    /// </param>
    public ScanState(AppRuntime runtime, GameInstall game, OverviewState overview, IHostEnvironment host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _runtime = runtime;
        _game = game;
        _overview = overview;
        _host = host;
        RunScan = (s, progress) => ScanChecks.Run(s, progress, _runtime.Logger);
    }

    /// <summary>
    /// Raised on the UI thread when a Scan begins, with <see cref="IsRunning"/> already true: the previous results are
    /// to be cleared.
    /// </summary>
    public event EventHandler? Started;

    /// <summary>
    /// Raised on the UI thread for every status the scanning label and the progress bar should show: the refresh's
    /// <c>Refreshing Overview...</c>, the bar at 1 once the refresh is done, each status the scan reports, and the bar at
    /// 100 when the scan is over.
    /// </summary>
    public event EventHandler<ScanStatus>? StatusChanged;

    /// <summary>
    /// Raised on the UI thread when a Scan ends, with <see cref="IsRunning"/> already false and any error the scan
    /// returned already in the Error Window.
    /// </summary>
    public event EventHandler<ScanEnded>? Ended;

    /// <summary>Whether a Scan is running: true from just before <see cref="Started"/> until just before <see cref="Ended"/>.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Everything in the Scan after its Overview refresh, run off the UI thread as an Interactive Operation and given the
    /// Scan's snapshot and its progress sink:
    /// <see cref="ScanChecks.Run(ScanSettings, IProgress{ScanStatus}?, ILogger)"/> with the app log by default. A test
    /// seam, so tests can hold a scan mid-run, report statuses or hand back a canned outcome.
    /// </summary>
    /// <remarks>
    /// The sink is a <see cref="UiThreadProgress{T}"/> made for each Scan, so it takes statuses from any thread and
    /// raises <see cref="StatusChanged"/> for each one on the UI thread before <see cref="IProgress{T}.Report"/> returns.
    /// Every status is therefore raised, in order, before <see cref="Ended"/>. A status reported once the scan has
    /// returned is dropped.
    /// </remarks>
    public Func<ScanSettings, IProgress<ScanStatus>, ScanOutcome> RunScan { get; init; }

    /// <summary>
    /// Runs one Scan: refreshes the Overview Report, takes the snapshot of <paramref name="boxes"/>, runs the scan with its
    /// statuses raised as it goes, reports any error it returned, then raises <see cref="Ended"/>. A UI Action: the Scan
    /// Game button starts it through <see cref="UiActions.Start"/>. While a Scan runs, a start does nothing but log at
    /// DEBUG.
    /// </summary>
    /// <param name="boxes">The Scan Settings' boxes as they stood when Scan Game was clicked; every setting present.</param>
    /// <returns>A task that completes once every <see cref="Ended"/> handler has returned, or at once when a Scan is running.</returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    /// <exception cref="Exception">
    /// Whatever an event handler threw, after <see cref="IsRunning"/> has gone back to false. A failed Operation doesn't
    /// throw: the Scan ends <see cref="ScanEnded.NotScanned"/> or listing the stopped-early row instead.
    /// </exception>
    public async Task StartAsync(IReadOnlyDictionary<ScanSetting, bool> boxes)
    {
        // Checked before anything else, so a start from another thread sets nothing and raises nothing: left to the
        // refresh's own check, it would already have set IsRunning and raised Started and a status off the UI thread.
        Dispatcher.UIThread.VerifyAccess();
        if (IsRunning)
        {
            // Scan Game is the only way in, and it is disabled while a Scan runs; this is the defensive path behind it,
            // which must not reach the Error Window.
            _runtime.Logger.Write(LogLevel.Debug, "Scanner : Scan : Already running, ignored");
            return;
        }

        IsRunning = true;
        ScanEnded ended;
        try
        {
            ended = await RunAsync(boxes);
        }
        finally
        {
            // Back to false before Ended's handlers run, and even when a handler threw, so a failed Scan can't leave every
            // later start ignored.
            IsRunning = false;
        }

        Ended?.Invoke(this, ended);
    }

    /// <summary>
    /// The Scan from <see cref="Started"/> up to <see cref="Ended"/>: the refresh, the snapshot, the scan, and the report
    /// of an error it returned.
    /// </summary>
    /// <returns>How the Scan ended, for <see cref="Ended"/>.</returns>
    private async Task<ScanEnded> RunAsync(IReadOnlyDictionary<ScanSetting, bool> boxes)
    {
        Started?.Invoke(this, EventArgs.Empty);

        // The bar stays where the last Scan left it while the Overview refreshes.
        Raise(new ScanStatus("Refreshing Overview...", _last.Progress));
        if (await _overview.TryRefreshAsync() is OperationOutcome<OverviewReport>.Failed)
        {
            // The runner has reported the error, and the outcome hands it back rather than ending the action, so the Scan
            // ends here, before anything is checked, and Scan Game can be used again.
            return new ScanEnded.NotScanned();
        }

        // The snapshot, taken after the refresh so it carries the new report. Each box was saved as it changed, so the
        // settings already hold these and nothing is written.
        var settings = ScanSettings.Snapshot(boxes, _game, _overview.Report, _host);

        // The label stays on Refreshing Overview... until the scan reports a status of its own, and it reports none when
        // only Overview Issues is ticked.
        Raise(_last with { Progress = 1 });

        // This Scan's own sink. The handler and the write to `over` below both run on the UI thread, so a status a
        // thread the scan left running reports after the scan returned sees `over` set and raises nothing. A sink per
        // Scan also keeps one Scan's flag from silencing another's.
        var over = false;
        var statuses = new UiThreadProgress<ScanStatus>(status =>
        {
            if (!over)
            {
                Raise(status);
            }
        });
        var run = await _runtime.Actions.TryRun(ScanOperation, OperationPhase.Interactive, () => RunScan(settings, statuses));
        over = true;

        ScanOutcome outcome;
        var errorReported = false;
        if (run is OperationOutcome<ScanOutcome>.Failed failed)
        {
            // The last resort, for an exception that escaped the scan, which returns its own failures on its outcome.
            // Everything the scan would have listed died with it (it seeds its own results), so the list is the
            // stopped-early row alone: an empty list would read as a clean scan. The runner has already reported the
            // error, so it must not reach the Error Window a second time below.
            outcome = ScanOutcome.StoppedEarly([], failed.Error);
            errorReported = true;
        }
        else
        {
            outcome = ((OperationOutcome<ScanOutcome>.Succeeded)run).Value;
        }

        if (outcome.Error is { } error && !errorReported)
        {
            // The scan stopped early and returned its error rather than throwing it, so no runner reported it. Shown
            // now, on the UI thread, so the Error Window is up before the partial results are listed.
            _runtime.Actions.ReportFailure(ScanOperation, error);
        }

        Raise(_last with { Progress = 100 });
        return new ScanEnded.Listed(settings, outcome);
    }

    /// <summary>Remembers <paramref name="status"/> as the last one and raises <see cref="StatusChanged"/> with it. UI thread only.</summary>
    private void Raise(ScanStatus status)
    {
        _last = status;
        StatusChanged?.Invoke(this, status);
    }
}
