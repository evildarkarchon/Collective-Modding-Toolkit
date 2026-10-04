using Avalonia.Threading;

namespace CMToolkit.Tests.Support;

/// <summary>Waits, on the UI thread, for work that finishes on a thread-pool worker and then posts back.</summary>
public static class DispatcherWait
{
    /// <summary>
    /// Runs dispatcher jobs until <paramref name="condition"/> holds. A single <see cref="Dispatcher.RunJobs"/> isn't
    /// enough when a background operation is involved: its continuation is only queued once the worker finishes, which
    /// on a cold or busy machine can be after that one drain.
    /// </summary>
    /// <exception cref="TimeoutException">The condition didn't hold within <see cref="TaskTimeouts.Default"/>.</exception>
    public static void Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TaskTimeouts.Default;
        while (true)
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The UI never reached the expected state.");
            }

            // The worker needs the CPU, not the UI thread; nothing is queued for us until it finishes.
            Thread.Sleep(5);
        }
    }
}
