using Avalonia.Threading;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The UI Action runner (ADR-0009), reached as <c>runtime.Actions</c>: the one place that starts a UI Action and
/// handles its failure. The action stops at its first failure, which goes to the Error Window, and whatever it skipped
/// is its Failure Outcome. Start every async UI Action through <see cref="Start"/>; never <c>_ = XAsync()</c> on an
/// event, because a discarded task bypasses the global handlers and its fault surfaces only if the garbage collector
/// raises it as unobserved, maybe never (ADR-0003).
/// </summary>
public sealed class UiActions
{
    private readonly ErrorReporter _errors;

    /// <param name="errors">Where an action's unreported failure goes.</param>
    public UiActions(ErrorReporter errors)
    {
        _errors = errors;
    }

    /// <summary>Starts a UI Action and reports its failure, if it has not been reported already.</summary>
    /// <remarks>
    /// <para>An <see cref="OperationFailedException"/> is swallowed: the runner already reported it, under the
    /// operation's own header, and propagating it is what skipped the rest of the action. Any other exception is
    /// reported under <see cref="ErrorReporter.UiThreadHeader"/>, synchronously, before the returned task completes.
    /// The Error Window must be up before deferred input (a queued Close or Escape) replays from its posted job, which
    /// is also why handlers don't rely on <c>async void</c> and the global hook: an <c>async void</c> rethrow arrives as
    /// a posted job, queued behind the replay.</para>
    /// <para>No name parameter: an operation that fails inside the action is still reported by the runner under its own
    /// name. No input block either: callers that need one hold their own, since theirs deliberately start after earlier
    /// steps. UI thread only.</para>
    /// </remarks>
    /// <param name="action">
    /// The action. A <see cref="Func{TResult}"/>, not a <see cref="Task"/>, so it is started here and a synchronous throw
    /// before its first await is caught too.
    /// </param>
    /// <returns>
    /// A task that completes when the action has finished or failed, and never faults, so callers may discard it and
    /// tests may await it. A faulting task would be reported a second time, through
    /// <see cref="TaskScheduler.UnobservedTaskException"/>, once discarded.
    /// </returns>
    public async Task Start(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationFailedException)
        {
            // The runner reported it when the operation died; propagating it is what skipped the rest of the action.
        }
        catch (Exception ex)
        {
            // Report, not Post: on the UI thread it shows the window now, ahead of any deferred input's posted replay.
            _errors.Report(ErrorReporter.UiThreadHeader, ex);
        }
    }

    /// <summary>
    /// Starts a UI Action the app schedules for itself, such as the Downgrader's next download poll: it is posted to the
    /// UI thread and started there through <see cref="Start"/>, once the UI thread gets to it, so it follows the same
    /// rules as a click. Safe to call from any thread.
    /// </summary>
    /// <param name="action">The action, started on the UI thread.</param>
    public void Schedule(Func<Task> action)
        // Start's task never faults, so the posted job can discard it.
        => Dispatcher.UIThread.Post(() => _ = Start(action));
}
