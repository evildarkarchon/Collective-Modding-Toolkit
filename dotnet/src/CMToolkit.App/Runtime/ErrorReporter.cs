using Avalonia.Threading;
using CMToolkit.App.Views;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Runtime;

/// <summary>
/// Reports unhandled exceptions, from any thread, to the Error Window and to the log, the moment they happen (issue
/// #18). The Reference Implementation surfaced some of these late and swallowed others (issue #40); the port surfaces
/// every one, which is the one deliberate deviation in error surfacing.
/// </summary>
/// <remarks>
/// A report is <c>&lt;header&gt;\n&lt;exception&gt;</c>. The header names the context, the way Python's
/// <c>Exception in Tkinter callback</c> and <c>Exception in thread …</c> lines did. The log line is
/// <c>StdErr : &lt;report&gt;</c> at ERROR, keeping the reference's <c>StdErr</c> prefix so people who grep logs still
/// find errors; the window shows the report alone, as the reference's window showed the bare traceback.
/// </remarks>
public sealed class ErrorReporter
{
    /// <summary>The header for an exception that escaped UI-thread code: an event handler, a binding, a posted job.</summary>
    public const string UiThreadHeader = "Exception on the UI thread";

    /// <summary>The header for a faulted task that nothing awaited.</summary>
    public const string UnobservedTaskHeader = "Unobserved exception in a background task";

    /// <summary>The header for an exception while the app was being built, before the main window was shown.</summary>
    public const string StartupHeader = "Unhandled exception during startup";

    /// <summary>The header for an exception that is about to end the process; there is no time left to show it.</summary>
    private const string ProcessExitingHeader = "Unhandled exception; the process is exiting";

    private readonly ILogger _logger;
    private ErrorWindow? _window;

    /// <param name="logger">The app log; must be safe to call from any thread, since reports come from workers.</param>
    public ErrorReporter(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>The open Error Window, or <see langword="null"/> if none is open.</summary>
    public ErrorWindow? Window => _window;

    /// <summary>The header for an exception that killed the background operation <paramref name="name"/>.</summary>
    public static string OperationHeader(string name) => $"Exception in background operation '{name}'";

    /// <summary>
    /// Logs the exception at once, on the calling thread, and shows it in the Error Window: at once on the UI thread,
    /// otherwise as soon as the UI thread gets to it. Opens the window if none is open, or appends to the open one.
    /// </summary>
    /// <param name="header">Names where the exception happened.</param>
    /// <param name="exception">The exception, shown with its type, message and stack trace.</param>
    public void Report(string header, Exception exception)
    {
        var report = Log(header, exception);
        if (Dispatcher.UIThread.CheckAccess())
        {
            Show(report);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Show(report));
        }
    }

    /// <summary>
    /// Logs the exception without opening the Error Window, for errors that have somewhere else to go (the startup
    /// failure dialog) or nowhere at all (the process is exiting). Thread-safe.
    /// </summary>
    /// <returns>The report text, as logged without its <c>StdErr</c> prefix.</returns>
    public string Log(string header, Exception exception)
    {
        var report = $"{header}\n{exception}";
        _logger.LogError("StdErr : {Report}", report);
        return report;
    }

    /// <summary>
    /// Routes every unhandled exception here until disposed. UI-thread exceptions are reported and marked handled, so
    /// the app survives them as Tk survived a failed callback. Unobserved task exceptions are reported and marked
    /// observed. An exception about to end the process is only logged. A <see cref="OperationFailedException"/> is
    /// dropped wherever it lands: the runner reported it when the operation died.
    /// </summary>
    /// <returns>Unhooks the handlers when disposed.</returns>
    public IDisposable InstallGlobalHandlers()
    {
        Dispatcher.UIThread.UnhandledException += OnUiThreadException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnProcessExiting;
        return new Unhook(this);
    }

    private void Show(string report)
    {
        if (_window is null)
        {
            var window = new ErrorWindow();
            // Closing discards the window and its text; the next error opens a fresh one.
            window.Closed += (_, _) =>
            {
                if (_window == window)
                {
                    _window = null;
                }
            };
            _window = window;
            window.Show();
        }

        _window.Append(report);
    }

    private void OnUiThreadException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (e.Exception is not OperationFailedException)
        {
            Report(UiThreadHeader, e.Exception);
        }

        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Raised on the finalizer thread; Report marshals the window part to the UI thread.
        var unreported = e.Exception.InnerExceptions.Where(ex => ex is not OperationFailedException).ToList();
        if (unreported.Count > 0)
        {
            Report(UnobservedTaskHeader, unreported.Count == 1 ? unreported[0] : new AggregateException(unreported));
        }

        e.SetObserved();
    }

    private void OnProcessExiting(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception and not OperationFailedException)
        {
            Log(ProcessExitingHeader, exception);
        }
    }

    /// <summary>Unhooks the global handlers. Disposing it twice unhooks them once.</summary>
    private sealed class Unhook(ErrorReporter owner) : IDisposable
    {
        private ErrorReporter? _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } reporter)
            {
                Dispatcher.UIThread.UnhandledException -= reporter.OnUiThreadException;
                TaskScheduler.UnobservedTaskException -= reporter.OnUnobservedTaskException;
                AppDomain.CurrentDomain.UnhandledException -= reporter.OnProcessExiting;
            }
        }
    }
}
