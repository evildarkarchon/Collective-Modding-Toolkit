using Avalonia.Threading;
using CMToolkit.Core;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The UI Action runner (ADR-0009), reached as <c>runtime.Actions</c>: the one place that starts a UI Action, runs its
/// Operations and handles its failure. The action stops at its first failure, which goes to the Error Window, and
/// whatever it skipped is its Failure Outcome. Start every async UI Action through <see cref="Start"/>; never
/// <c>_ = XAsync()</c> on an event, because a discarded task bypasses the global handlers and its fault surfaces only if
/// the garbage collector raises it as unobserved, maybe never (ADR-0003).
/// </summary>
/// <remarks>
/// Every Core call is an Operation started through <see cref="Run(string, OperationPhase, Action)"/> or
/// <see cref="RunPhased"/>: the work runs on the thread pool, so the window never stops painting, and the Operation's
/// <see cref="OperationPhase"/> decides whether the UI stays live meanwhile. An exception in the work is reported to the
/// Error Window and the log, then the returned task fails with <see cref="OperationFailedException"/>. Letting that
/// propagate is what stops the UI Action at its first failure; <see cref="Start"/> and the global handlers know it was
/// already reported. The few actions that must carry on after a failed Operation start it through
/// <see cref="TryRun{T}"/> instead, which hands the reported failure back as an <see cref="OperationOutcome{T}"/>; no
/// caller catches the exception, which is internal to the runner. Every method but <see cref="Schedule"/> is called on
/// the UI thread, and its results come back on it.
/// </remarks>
public sealed class UiActions
{
    private readonly ErrorReporter _errors;
    private readonly InputBlocker _input;

    /// <param name="errors">Where an action's or an Operation's unreported failure goes.</param>
    /// <param name="input">The input block that <see cref="OperationPhase.Blocking"/> phases hold.</param>
    public UiActions(ErrorReporter errors, InputBlocker input)
    {
        _errors = errors;
        _input = input;
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
    /// name. No input block either: an action that needs one wraps the steps it covers in <see cref="Blocking"/>, since
    /// a block often deliberately starts after earlier steps. UI thread only.</para>
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

    /// <summary>Runs <paramref name="work"/> off the UI thread as one Operation in a single phase.</summary>
    /// <param name="name">Names the Operation in the Error Window if it fails.</param>
    /// <param name="phase">Whether the UI is input-blocked while it runs.</param>
    /// <param name="work">The Core call.</param>
    /// <exception cref="OperationFailedException">The work threw; the error has been reported.</exception>
    public Task Run(string name, OperationPhase phase, Action work)
        => RunPhased<object?>(name, phase, _ =>
        {
            work();
            return Task.FromResult<object?>(null);
        });

    /// <inheritdoc cref="Run(string, OperationPhase, Action)"/>
    /// <returns>The work's result, back on the UI thread.</returns>
    public Task<T> Run<T>(string name, OperationPhase phase, Func<T> work)
        => RunPhased(name, phase, _ => Task.FromResult(work()));

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread as one Operation that switches between phases as it goes, like
    /// the Downgrader's run. Each phase marker the work reports is applied on the UI thread, in order, and
    /// <see cref="IProgress{T}.Report"/> returns only once it has been: after reporting
    /// <see cref="OperationPhase.Blocking"/>, the work can rely on input already being blocked.
    /// </summary>
    /// <param name="name">Names the Operation in the Error Window if it fails.</param>
    /// <param name="initialPhase">The phase it starts in, applied before this method returns.</param>
    /// <param name="work">The Core operation. It receives the sink for its phase markers, which it must only call from
    /// its own worker thread or the UI thread; the UI thread must never wait synchronously on the work.</param>
    /// <returns>The work's result, back on the UI thread.</returns>
    /// <exception cref="OperationFailedException">The work threw; the error has been reported.</exception>
    public async Task<T> RunPhased<T>(
        string name, OperationPhase initialPhase, Func<IProgress<OperationPhase>, Task<T>> work)
    {
        var outcome = await TryRunPhased(name, initialPhase, work);
        outcome.ThrowIfFailed();
        return ((OperationOutcome<T>.Succeeded)outcome).Value;
    }

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread as one Operation in a single phase, like
    /// <see cref="Run{T}"/>, but hands its failure back instead of throwing it, for a UI Action that must carry on after
    /// it. The failure is still reported, once, under the Operation's name.
    /// </summary>
    /// <param name="name">Names the Operation in the Error Window if it fails.</param>
    /// <param name="phase">Whether the UI is input-blocked while it runs.</param>
    /// <param name="work">The Core call.</param>
    /// <returns>
    /// <see cref="OperationOutcome{T}.Succeeded"/> with the work's result, or <see cref="OperationOutcome{T}.Failed"/>
    /// with what it threw, already reported; back on the UI thread either way.
    /// </returns>
    /// <exception cref="InvalidOperationException">Not called on the UI thread.</exception>
    public Task<OperationOutcome<T>> TryRun<T>(string name, OperationPhase phase, Func<T> work)
        => TryRunPhased(name, phase, _ => Task.FromResult(work()));

    /// <summary>
    /// Reports an error that an Operation returned rather than threw, such as the Scan's when it stops early, under the
    /// header the runner gives that Operation's failures. Synchronous, so on the UI thread the Error Window is up before
    /// this returns.
    /// </summary>
    /// <param name="name">The Operation's name, as given to the runner.</param>
    /// <param name="error">The error it returned.</param>
    public void ReportFailure(string name, Exception error)
        => _errors.Report(ErrorReporter.OperationHeader(name), error);

    /// <summary>
    /// The one place an Operation runs: off the UI thread, in its phases, with a failure reported under its name and
    /// handed back as <see cref="OperationOutcome{T}.Failed"/>. The throwing forms rethrow from here.
    /// </summary>
    private async Task<OperationOutcome<T>> TryRunPhased<T>(
        string name, OperationPhase initialPhase, Func<IProgress<OperationPhase>, Task<T>> work)
    {
        Dispatcher.UIThread.VerifyAccess();

        // Disposed after the catch block has reported, so the Error Window is up before deferred input is replayed.
        using var phases = new PhaseTracker(_input, initialPhase);
        try
        {
            return new OperationOutcome<T>.Succeeded(await Task.Run(() => work(phases)));
        }
        catch (Exception ex)
        {
            _errors.Report(ErrorReporter.OperationHeader(name), ex);
            return new OperationOutcome<T>.Failed(name, ex);
        }
    }

    /// <summary>
    /// Runs <paramref name="span"/>, a stretch of a UI Action, with input blocked throughout: across the Operations in
    /// it, whatever their phases, and the UI-thread steps between them, so no click, key, Close or Escape lands in the
    /// middle. Deferred Close and Escape replay once the span ends.
    /// </summary>
    /// <remarks>
    /// If the span throws an error the runner hasn't reported, it is reported under
    /// <see cref="ErrorReporter.UiThreadHeader"/> before the block is released, so the Error Window is up before deferred
    /// input replays. An <see cref="OperationFailedException"/> was reported when its Operation died, and is let through
    /// as it is. UI thread only.
    /// </remarks>
    /// <param name="span">
    /// The steps to run under the block. A <see cref="Func{TResult}"/>, so a synchronous throw before its first await is
    /// handled the same way.
    /// </param>
    /// <exception cref="OperationFailedException">
    /// The span failed; the error has been reported, whether by the Operation that died or by this method.
    /// </exception>
    public async Task Blocking(Func<Task> span)
    {
        Dispatcher.UIThread.VerifyAccess();

        var block = _input.Block();
        try
        {
            await span();
        }
        catch (OperationFailedException)
        {
            // Already reported by the Operation that died; reporting it again would show it twice.
            throw;
        }
        catch (Exception ex)
        {
            // Here, not in the caller: the report must come before the finally block releases the input block, because
            // the release is what posts a deferred Close or Escape's replay.
            _errors.Report(ErrorReporter.UiThreadHeader, ex);
            throw new OperationFailedException(ex);
        }
        finally
        {
            block.Dispose();
        }
    }

    /// <summary>
    /// Maps an operation's phase markers onto the input block: it holds a block exactly while the current phase is
    /// <see cref="OperationPhase.Blocking"/>. Touched only on the UI thread; a report from the worker is marshalled
    /// there, and the worker waits for it.
    /// </summary>
    private sealed class PhaseTracker : IProgress<OperationPhase>, IDisposable
    {
        private readonly InputBlocker _input;
        private IDisposable? _block;
        private bool _ended;

        public PhaseTracker(InputBlocker input, OperationPhase initialPhase)
        {
            _input = input;
            Apply(initialPhase);
        }

        public void Report(OperationPhase value)
        {
            // Invoke, not Post: the worker waits until the UI thread has applied the phase. With a post, the worker
            // would start blocking work while the block was still queued, so a click, Escape or close could land in
            // work that must run with input blocked. Waiting can't deadlock, because the runner awaits the work
            // rather than blocking the UI thread on it. A marker reported after the work has ended is ignored.
            Dispatcher.UIThread.Invoke(() => Apply(value));
        }

        public void Dispose()
        {
            _ended = true;
            _block?.Dispose();
            _block = null;
        }

        private void Apply(OperationPhase phase)
        {
            if (_ended)
            {
                return;
            }

            if (phase == OperationPhase.Blocking)
            {
                _block ??= _input.Block();
            }
            else
            {
                _block?.Dispose();
                _block = null;
            }
        }
    }
}
