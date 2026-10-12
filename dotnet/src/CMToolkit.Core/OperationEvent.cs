namespace CMToolkit.Core;

/// <summary>
/// What a log pane line is (<c>enums.LogType</c>), which picks its emoji, the emoji's colour and the level it is
/// logged at: the Downgrader's and Archive Patcher's log pane (§9.5).
/// </summary>
public enum LogType
{
    /// <summary>A thought-balloon emoji in dodger blue; logged at INFO.</summary>
    Info,

    /// <summary>A check-mark emoji in the good colour; logged at INFO.</summary>
    Good,

    /// <summary>A cross-mark emoji in the bad colour; logged at ERROR.</summary>
    Bad,
}

/// <summary>
/// One thing a long Core operation (the Archive Patcher, the Downgrader) reports to the App while it runs, in the
/// reference's order, through an <see cref="IProgress{T}"/> (the Core-App contract of issue #7).
/// </summary>
public abstract record OperationEvent;

/// <summary>
/// A line for the operation's log pane: <c>Logger.log_message(type, text, skip_logging=...)</c>. The App shows it, and
/// writes it to the app log unless <paramref name="WriteToLogFile"/> is false (LOG-1).
/// </summary>
/// <param name="Type">The line's kind: its emoji, colour and log level.</param>
/// <param name="Text">The text after the emoji.</param>
/// <param name="WriteToLogFile">False for the reference's <c>skip_logging=True</c>.</param>
public sealed record LogLine(LogType Type, string Text, bool WriteToLogFile = true) : OperationEvent;

/// <summary>
/// Where the operation's progress bar now stands, out of 100: the Downgrader's <c>progress_var.set(value)</c>, set
/// directly or from the download's progress queue at each 100 ms poll.
/// </summary>
/// <param name="Value">The bar's new value, 0 to 100 (a download's <c>downloaded / content-length * 100</c>).</param>
public sealed record Percent(double Value) : OperationEvent;
