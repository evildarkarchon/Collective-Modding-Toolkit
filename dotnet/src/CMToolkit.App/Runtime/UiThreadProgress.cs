using Avalonia.Threading;

namespace CMToolkit.App.Runtime;

/// <summary>
/// An <see cref="IProgress{T}"/> for a Core operation running under <see cref="BackgroundOperations"/>: each report is
/// handled on the UI thread before <see cref="Report"/> returns, so the App sees a run's events (log pane lines,
/// percentages) in the order the work emitted them, each before the work goes on, as the reference's UI-thread code
/// handled them in line.
/// </summary>
/// <remarks>
/// Unlike <see cref="Progress{T}"/>, which posts and returns, this waits: a posted event could still be queued when the
/// operation's own completion runs, so the run's summary line could follow the code that comes after the run. Waiting
/// can't deadlock, because the runner awaits the work rather than blocking the UI thread on it.
/// </remarks>
/// <typeparam name="T">The event type.</typeparam>
/// <param name="handle">Handles one event on the UI thread.</param>
public sealed class UiThreadProgress<T>(Action<T> handle) : IProgress<T>
{
    /// <inheritdoc/>
    public void Report(T value)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            handle(value);
        }
        else
        {
            Dispatcher.UIThread.Invoke(() => handle(value));
        }
    }
}
