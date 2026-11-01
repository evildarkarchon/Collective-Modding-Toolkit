using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Overview;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.ViewModels;

/// <summary>
/// The Overview Report module: it owns the current <see cref="OverviewReport"/>, which the Overview, F4SE, Scanner,
/// Downgrader and Archive Patcher read. Only its <see cref="LoadAsync"/> and <see cref="RefreshAsync"/> replace it; until
/// the first load there is none, and the Install Type is <c>Unknown</c>.
/// </summary>
/// <remarks>
/// Each gathering runs as a Blocking operation through the runner, which reports a failure once, under the operation's
/// name, and throws its <see cref="OperationFailedException"/>. A gathering that dies part-way still leaves what it had
/// gathered as the current report before the failure propagates, so readers work from the best report there is, and
/// each caller decides what its own failure skips. Start both operations on the UI thread.
/// </remarks>
public sealed partial class OverviewState : ObservableObject
{
    private readonly AppRuntime _runtime;
    private readonly Func<OverviewReport?, OverviewReport> _gather;

    /// <summary>The module, with no report until its first load.</summary>
    /// <param name="runtime">The runner the gatherings run through, and the log the refresh line goes to.</param>
    /// <param name="gather">
    /// Gathers a new report from the previous one (null on the first load), on a worker thread. In the app it is
    /// <see cref="OverviewReport.Gather(GameInstall, IHostEnvironment, ILogger, OverviewReport?)"/> over the session's
    /// game, machine and log; tests swap in fakes. It may throw <see cref="OverviewIncompleteException"/> to hand back
    /// what it had gathered before it died.
    /// </param>
    public OverviewState(AppRuntime runtime, Func<OverviewReport?, OverviewReport> gather)
    {
        _runtime = runtime;
        _gather = gather;
    }

    /// <summary>
    /// Raised on the UI thread after a refresh has replaced <see cref="Report"/>. Not raised by the load, nor by a refresh
    /// that failed, even one that left a partial report current.
    /// </summary>
    public event EventHandler? Refreshed;

    /// <summary>The current report, or null before the first load (or after a first load that gathered nothing).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallType))]
    public partial OverviewReport? Report { get; private set; }

    /// <summary>The game's Install Type, bound live by the Overview tab's Version label.</summary>
    public InstallType InstallType => Report?.InstallType ?? InstallType.Unknown;

    /// <summary>Gathers the first report as the Blocking operation <c>Overview load</c>.</summary>
    /// <returns>A task that completes once the report is current.</returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    /// <exception cref="OperationFailedException">
    /// The gathering failed; the runner has reported it, and any partial report it carried is current.
    /// </exception>
    public async Task LoadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        Report = await GatherAsync("Overview load", previous: null);
    }

    /// <summary>
    /// Logs <c>Overview : Refresh</c> at DEBUG, gathers a new report from the current one as the Blocking operation
    /// <c>Overview refresh</c>, makes it current and raises <see cref="Refreshed"/>.
    /// </summary>
    /// <returns>
    /// A task that completes once the new report is current and every <see cref="Refreshed"/> handler has returned.
    /// </returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    /// <exception cref="OperationFailedException">
    /// The gathering failed; the runner has reported it, any partial report it carried is current, and
    /// <see cref="Refreshed"/> was not raised.
    /// </exception>
    public async Task RefreshAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _runtime.Logger.Write(LogLevel.Debug, "Overview : Refresh");
        Report = await GatherAsync("Overview refresh", Report);
        Refreshed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs the gather function as the Blocking operation <paramref name="name"/>. A gathering that died part-way makes
    /// its partial report current before the runner's failure propagates.
    /// </summary>
    private async Task<OverviewReport> GatherAsync(string name, OverviewReport? previous)
    {
        try
        {
            return await _runtime.Operations.RunAsync(name, OperationPhase.Blocking, () => _gather(previous));
        }
        catch (OperationFailedException ex) when (ex.InnerException is OverviewIncompleteException incomplete)
        {
            // The runner has reported it; what the gathering had got to is still the best report there is.
            Report = incomplete.Partial;
            throw;
        }
    }
}
