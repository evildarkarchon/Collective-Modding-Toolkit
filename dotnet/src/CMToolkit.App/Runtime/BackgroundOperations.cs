using Avalonia.Threading;
using CMToolkit.Core;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The background-operation runner (ADR-0003). Every Core call goes through it: the work runs on the thread pool, so
/// the window never stops painting, and the operation's <see cref="OperationPhase"/> decides whether the UI stays live
/// meanwhile. Start operations on the UI thread; their results come back on it.
/// </summary>
/// <remarks>
/// An exception in the work is reported to the Error Window and the log, then the returned task fails with
/// <see cref="OperationFailedException"/>. That kills only this operation: the rest of the caller's UI code is skipped,
/// as an exception skipped the rest of a Tk callback, so its Failure Outcome (a tab stuck on its loading text, a
/// button left disabled) happens as in the reference. The global handlers know the exception was already reported.
/// </remarks>
public sealed class BackgroundOperations
{
    private readonly ErrorReporter _errors;
    private readonly InputBlocker _input;

    /// <param name="errors">Where a failing operation's exception is reported.</param>
    /// <param name="input">The input block that <see cref="OperationPhase.Blocking"/> phases hold.</param>
    public BackgroundOperations(ErrorReporter errors, InputBlocker input)
    {
        _errors = errors;
        _input = input;
    }

    /// <summary>Runs <paramref name="work"/> off the UI thread as one operation in a single phase.</summary>
    /// <param name="name">Names the operation in the Error Window if it fails.</param>
    /// <param name="phase">Whether the UI is input-blocked while it runs.</param>
    /// <param name="work">The Core call.</param>
    /// <exception cref="OperationFailedException">The work threw; the error has been reported.</exception>
    public Task RunAsync(string name, OperationPhase phase, Action work)
        => RunPhasedAsync<object?>(name, phase, _ =>
        {
            work();
            return Task.FromResult<object?>(null);
        });

    /// <inheritdoc cref="RunAsync(string, OperationPhase, Action)"/>
    /// <returns>The work's result, back on the UI thread.</returns>
    public Task<T> RunAsync<T>(string name, OperationPhase phase, Func<T> work)
        => RunPhasedAsync(name, phase, _ => Task.FromResult(work()));

    /// <summary>
    /// Runs <paramref name="work"/> off the UI thread as one operation that switches between phases as it goes, like
    /// the Downgrader's run. Each phase marker the work reports is applied on the UI thread, in order, and
    /// <see cref="IProgress{T}.Report"/> returns only once it has been: after reporting
    /// <see cref="OperationPhase.Blocking"/>, the work can rely on input already being blocked.
    /// </summary>
    /// <param name="name">Names the operation in the Error Window if it fails.</param>
    /// <param name="initialPhase">The phase it starts in, applied before this method returns.</param>
    /// <param name="work">The Core operation. It receives the sink for its phase markers, which it must only call from
    /// its own worker thread or the UI thread; the UI thread must never wait synchronously on the work.</param>
    /// <returns>The work's result, back on the UI thread.</returns>
    /// <exception cref="OperationFailedException">The work threw; the error has been reported.</exception>
    public async Task<T> RunPhasedAsync<T>(
        string name, OperationPhase initialPhase, Func<IProgress<OperationPhase>, Task<T>> work)
    {
        Dispatcher.UIThread.VerifyAccess();

        // Disposed after the catch block has reported, so the Error Window is up before deferred input is replayed.
        using var phases = new PhaseTracker(_input, initialPhase);
        try
        {
            return await Task.Run(() => work(phases));
        }
        catch (Exception ex)
        {
            _errors.Report(ErrorReporter.OperationHeader(name), ex);
            throw new OperationFailedException(name, ex);
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
            // work the reference ran with the UI frozen. Waiting can't deadlock, because the runner awaits the work
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
