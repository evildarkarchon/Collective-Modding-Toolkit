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
    /// The app log: the <c>cm-toolkit.log</c> <see cref="LogFile"/> at startup (a null logger only if it couldn't be
    /// opened, and then startup fails), or a capturing logger in tests.
    /// </param>
    public AppRuntime(ILogger logger)
    {
        Logger = logger;
        Input = new InputBlocker();
        Errors = new ErrorReporter(logger, Input);
        Operations = new BackgroundOperations(Errors, Input);
    }

    /// <summary>The app log.</summary>
    public ILogger Logger { get; }

    /// <summary>Reports unhandled exceptions to the Error Window and the log.</summary>
    public ErrorReporter Errors { get; }

    /// <summary>
    /// The input block that blocking operations hold over the main window, its modals, the Error Window and every window
    /// they own.
    /// </summary>
    public InputBlocker Input { get; }

    /// <summary>The runner every Core call goes through.</summary>
    public BackgroundOperations Operations { get; }

    /// <summary>Where links open (NET-4): the user's browser, or a recorder in tests.</summary>
    public IBrowser Browser { get; init; } = ShellBrowser.Instance;

    /// <summary>
    /// The clock behind the UI's timed reverts, such as the <c>Copied!</c> buttons' <c>after(3000)</c> (THR-3). Tests
    /// swap in a fake they can advance.
    /// </summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;
}
