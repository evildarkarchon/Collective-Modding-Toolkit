using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The App infrastructure every window and tab shares: the log, the Error Window, the input block and the
/// background-operation runner. One instance is built at startup and passed down explicitly, so headless tests can
/// each run on their own.
/// </summary>
public sealed class AppRuntime
{
    /// <param name="logger">
    /// The app log. The Settings-and-log-file slice supplies the <c>cm-toolkit.log</c> provider; until then the app
    /// passes a null logger.
    /// </param>
    public AppRuntime(ILogger logger)
    {
        Logger = logger;
        Errors = new ErrorReporter(logger);
        Input = new InputBlocker();
        Operations = new BackgroundOperations(Errors, Input);
    }

    /// <summary>The app log.</summary>
    public ILogger Logger { get; }

    /// <summary>Reports unhandled exceptions to the Error Window and the log.</summary>
    public ErrorReporter Errors { get; }

    /// <summary>The input block that blocking operations hold over the main window and its modals.</summary>
    public InputBlocker Input { get; }

    /// <summary>The runner every Core call goes through.</summary>
    public BackgroundOperations Operations { get; }
}
