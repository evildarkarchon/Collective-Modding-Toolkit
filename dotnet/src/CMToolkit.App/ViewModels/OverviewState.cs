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
/// Downgrader and Archive Patcher read. Only its load and refresh replace it; until the first load there is none, and the
/// Install Type is <c>Unknown</c>.
/// </summary>
/// <remarks>
/// Each gathering runs as a Blocking operation through the runner, which reports a failure once, under the operation's
/// name. <see cref="LoadAsync"/> and <see cref="RefreshAsync"/> then throw it, which ends the caller's UI Action;
/// <see cref="TryLoadAsync"/> and <see cref="TryRefreshAsync"/> hand it back as an <see cref="OperationOutcome{T}"/>, for
/// a caller that carries on. Either way, a gathering that dies part-way still leaves what it had gathered as the current
/// report, so readers work from the best report there is, and each caller decides what its own failure skips. Start
/// every load and refresh on the UI thread.
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
    /// <remarks>
    /// Each handler runs as its own UI Action: one that throws is reported to the Error Window, the handlers after it
    /// still run, and the refresh still succeeds, so a reader's failure never reaches whoever asked for the refresh.
    /// </remarks>
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
    public async Task LoadAsync() => (await TryLoadAsync()).ThrowIfFailed();

    /// <summary>
    /// Gathers the first report as the Blocking operation <c>Overview load</c>, like <see cref="LoadAsync"/>, but hands
    /// a failure back instead of throwing it, for a caller that carries on after it.
    /// </summary>
    /// <returns>
    /// <see cref="OperationOutcome{T}.Succeeded"/> with the report, now current; or
    /// <see cref="OperationOutcome{T}.Failed"/>, already reported, with any partial report it carried current.
    /// </returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    public async Task<OperationOutcome<OverviewReport>> TryLoadAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        return await GatherAsync("Overview load", previous: null);
    }

    /// <summary>
    /// Logs <c>Overview : Refresh</c> at DEBUG, gathers a new report from the current one as the Blocking operation
    /// <c>Overview refresh</c>, makes it current and raises <see cref="Refreshed"/>.
    /// </summary>
    /// <returns>
    /// A task that completes once the new report is current and every <see cref="Refreshed"/> handler has returned or
    /// thrown. A handler that threw has been reported to the Error Window; its exception doesn't fail the refresh.
    /// </returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    /// <exception cref="OperationFailedException">
    /// The gathering failed; the runner has reported it, any partial report it carried is current, and
    /// <see cref="Refreshed"/> was not raised.
    /// </exception>
    public async Task RefreshAsync() => (await TryRefreshAsync()).ThrowIfFailed();

    /// <summary>
    /// Refreshes the report like <see cref="RefreshAsync"/>, but hands a failed gathering back instead of throwing it,
    /// for a caller that carries on after it.
    /// </summary>
    /// <returns>
    /// <see cref="OperationOutcome{T}.Succeeded"/> with the new report, once it is current and every
    /// <see cref="Refreshed"/> handler has returned or thrown; or <see cref="OperationOutcome{T}.Failed"/>, already
    /// reported, with any partial report it carried current and <see cref="Refreshed"/> not raised.
    /// </returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    public async Task<OperationOutcome<OverviewReport>> TryRefreshAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        _runtime.Logger.Write(LogLevel.Debug, "Overview : Refresh");
        var outcome = await GatherAsync("Overview refresh", Report);
        if (outcome is OperationOutcome<OverviewReport>.Succeeded)
        {
            await RaiseRefreshedAsync();
        }

        return outcome;
    }

    /// <summary>
    /// Runs each <see cref="Refreshed"/> handler in turn as its own UI Action through <see cref="UiActions.Start"/>,
    /// so a handler that throws is reported once to the Error Window and neither stops the handlers after it nor reaches
    /// the caller that asked for the refresh.
    /// </summary>
    private async Task RaiseRefreshedAsync()
    {
        if (Refreshed is not { } refreshed)
        {
            return;
        }

        // Walk the invocation list by hand: a plain Invoke stops at the first handler that throws.
        foreach (var handler in refreshed.GetInvocationList().Cast<EventHandler>())
        {
            await _runtime.Actions.Start(() =>
            {
                handler(this, EventArgs.Empty);
                return Task.CompletedTask;
            });
        }
    }

    /// <summary>
    /// Runs the gather function as the Blocking operation <paramref name="name"/> and makes what it gathered current: the
    /// new report, or, from a gathering that died part-way, its partial report. A gathering that died with nothing
    /// gathered leaves the current report as it was.
    /// </summary>
    private async Task<OperationOutcome<OverviewReport>> GatherAsync(string name, OverviewReport? previous)
    {
        var outcome = await _runtime.Actions.TryRun(name, OperationPhase.Blocking, () => _gather(previous));
        if (outcome is OperationOutcome<OverviewReport>.Succeeded succeeded)
        {
            Report = succeeded.Value;
        }
        else if (outcome is OperationOutcome<OverviewReport>.Failed { Error: OverviewIncompleteException incomplete })
        {
            // The runner has reported it; what the gathering had got to is still the best report there is.
            Report = incomplete.Partial;
        }

        return outcome;
    }
}
