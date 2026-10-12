using Microsoft.Extensions.Logging;

namespace CMToolkit.Core;

/// <summary>Writing finished log lines, without message templates.</summary>
public static class PlainLog
{
    /// <summary>
    /// Logs <paramref name="message"/> exactly as given. The <c>Log*</c> extension methods would parse it as a message
    /// template, where a <c>{</c> or <c>}</c> taken from a file (a settings key, a path) changes or breaks the text; the
    /// reference's messages are already rendered with Python's <c>%s</c> before they get here.
    /// </summary>
    /// <param name="logger">The log.</param>
    /// <param name="level">The level, which the log file renders with Python's level names.</param>
    /// <param name="message">The rendered message, such as <c>Settings : Saving settings.json</c>.</param>
    /// <param name="exception">The exception for a Python <c>logger.exception(...)</c> call, whose traceback follows the line.</param>
    public static void Write(this ILogger logger, LogLevel level, string message, Exception? exception = null)
        => logger.Log(level, default, message, exception, static (text, _) => text);
}
