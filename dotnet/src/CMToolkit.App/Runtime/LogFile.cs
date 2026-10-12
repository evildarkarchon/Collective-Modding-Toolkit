using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using CMToolkit.Core;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Runtime;

/// <summary>
/// <c>cm-toolkit.log</c>: the reference's one root logger, as <c>logging.basicConfig(filename=..., format="%(levelname)s
/// : %(message)s", level=INFO)</c> set it up (SHELL-1). Every line is <c>LEVEL : message</c> with Python's level names,
/// followed by the exception's text when one was logged. Thread-safe; background operations log from pool threads.
/// </summary>
/// <remarks>
/// <para>
/// The file is opened once, in append mode, and kept open for the life of the process; each line is flushed as it is
/// written, as Python's <c>FileHandler</c> does. It is written as Python's default text mode wrote it on Windows: in the
/// ANSI code page (<c>GetACP</c>, Python's locale encoding) with <c>backslashreplace</c> for characters the code page
/// lacks, and every <c>\n</c> as CRLF.
/// </para>
/// <para>
/// The level starts at INFO, so the three start lines and anything logged while settings load are filtered at INFO
/// whatever <c>log_level</c> says; startup then sets <see cref="MinimumLevel"/> from the setting (SHELL-2).
/// </para>
/// </remarks>
public sealed partial class LogFile : ILogger, IDisposable
{
    private readonly Lock _gate = new();
    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private volatile int _minimumLevel = (int)LogLevel.Information;
    private bool _disposed;

    private LogFile(Stream stream, Encoding encoding)
    {
        _stream = stream;
        _encoding = encoding;
    }

    /// <summary>
    /// The lowest level written. INFO until startup applies <c>log_level</c> (<c>logger.setLevel</c>, SHELL-2).
    /// </summary>
    public LogLevel MinimumLevel
    {
        get => (LogLevel)_minimumLevel;
        set => _minimumLevel = (int)value;
    }

    /// <summary>
    /// Opens (or creates) the log for appending, as <c>logging.basicConfig</c> did at the top of <c>main.py</c>.
    /// </summary>
    /// <param name="path">The log file; the App passes the CWD-relative <see cref="WorkingDirectoryFiles.Log"/>.</param>
    /// <exception cref="IOException">The file can't be opened, for example in a read-only folder.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file or folder is denied.</exception>
    /// <remarks>
    /// The reference dies here with PyInstaller's error dialog if the file can't be opened, before any window exists,
    /// so the App turns a throw into the startup failure dialog (SHELL-13).
    /// </remarks>
    public static LogFile Open(string path) => Open(path, AnsiCodePage());

    /// <summary>Opens the log for appending, encoded in code page <paramref name="codePage"/>.</summary>
    /// <param name="path">The log file.</param>
    /// <param name="codePage">The Windows code page to write in; <see cref="Open(string)"/> passes the ANSI code page.</param>
    public static LogFile Open(string path, int codePage)
    {
        // The CRT's default sharing, which Python's open() uses: other processes can read and write it meanwhile.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new LogFile(stream, EncodingFor(codePage));
    }

    /// <summary>The name Python's <c>logging</c> gives a level: <c>DEBUG</c>, <c>INFO</c>, <c>WARNING</c>, <c>ERROR</c>, <c>CRITICAL</c>.</summary>
    /// <remarks>Python has no level below DEBUG, so <see cref="LogLevel.Trace"/> is written as DEBUG too.</remarks>
    public static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace or LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARNING",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRITICAL",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>
    /// Writes <c>main.py</c>'s three start lines at INFO (SHELL-1): a rule of dashes as long as the start message,
    /// <c>Starting Collective Modding Toolkit v&lt;version&gt;</c>, and <paramref name="now"/> as <c>%Y-%m-%d %H:%M</c>.
    /// </summary>
    /// <param name="now">The local time to stamp, normally <see cref="DateTime.Now"/>.</param>
    public void WriteStartLines(DateTime now)
    {
        var start = $"Starting {AppInfo.Name} v{AppInfo.Version}";
        this.Write(LogLevel.Information, new string('-', start.Length));
        this.Write(LogLevel.Information, start);
        this.Write(LogLevel.Information, now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= MinimumLevel;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var text = $"{LevelName(logLevel)} : {formatter(state, exception)}";
        if (exception is not null)
        {
            // Python's formatter puts the traceback on the lines after the message.
            text += "\n" + exception;
        }

        // .NET's own text (exception traces) breaks lines with CRLF where Python's breaks with LF, so CRLF is folded to
        // LF first; then every LF is written as CRLF, as Python's text-mode file does.
        var line = (text + "\n").Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var bytes = _encoding.GetBytes(line);
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                _stream.Write(bytes);
                _stream.Flush();
            }
            catch (IOException)
            {
                // Python's Handler.handleError reports to sys.stderr, which is None in the windowed exe: a failed write
                // (a full disk, say) is lost silently and the app carries on.
            }
        }
    }

    /// <summary>Closes the file. Later log calls are dropped.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _stream.Dispose();
            }
        }
    }

    /// <summary>
    /// Code page <paramref name="codePage"/> with Python's <c>backslashreplace</c> for unencodable characters, the
    /// error handler <c>logging.basicConfig</c> gives its file. UTF-8 (65001, the "Beta: use Unicode UTF-8" setting)
    /// only meets it for lone surrogates. An unknown code page falls back to UTF-8.
    /// </summary>
    private static Encoding EncodingFor(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            return Encoding.GetEncoding(codePage, new BackslashReplaceFallback(), DecoderFallback.ReplacementFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.GetEncoding(65001, new BackslashReplaceFallback(), DecoderFallback.ReplacementFallback);
        }
    }

    /// <summary>The system ANSI code page, which is what Python's <c>locale.getencoding()</c> returns on Windows.</summary>
    private static int AnsiCodePage() => OperatingSystem.IsWindows() ? (int)GetACP() : 65001;

    [LibraryImport("kernel32.dll")]
    private static partial uint GetACP();

    /// <summary>
    /// Python's <c>backslashreplace</c> encode error handler: <c>\xNN</c> below U+0100, <c>\uNNNN</c> in the BMP
    /// (including a lone surrogate), <c>\UNNNNNNNN</c> beyond it, all lowercase hex.
    /// </summary>
    /// <remarks>
    /// Python's own cp1252 table leaves 0x81, 0x8D, 0x8F, 0x90 and 0x9D undefined, so it escapes U+0081 and friends,
    /// while Windows' table (used here) maps them to those bytes. Only those five C1 controls differ.
    /// </remarks>
    private sealed class BackslashReplaceFallback : EncoderFallback
    {
        public override int MaxCharCount => 10;

        public override EncoderFallbackBuffer CreateFallbackBuffer() => new Buffer();

        private sealed class Buffer : EncoderFallbackBuffer
        {
            private string _replacement = string.Empty;
            private int _position;

            public override int Remaining => _replacement.Length - _position;

            public override bool Fallback(char charUnknown, int index)
            {
                _replacement = charUnknown <= 0xff
                    ? string.Create(CultureInfo.InvariantCulture, $"\\x{(int)charUnknown:x2}")
                    : string.Create(CultureInfo.InvariantCulture, $"\\u{(int)charUnknown:x4}");
                _position = 0;
                return true;
            }

            public override bool Fallback(char charUnknownHigh, char charUnknownLow, int index)
            {
                _replacement = string.Create(
                    CultureInfo.InvariantCulture, $"\\U{char.ConvertToUtf32(charUnknownHigh, charUnknownLow):x8}");
                _position = 0;
                return true;
            }

            public override char GetNextChar() => _position < _replacement.Length ? _replacement[_position++] : '\0';

            public override bool MovePrevious()
            {
                if (_position == 0)
                {
                    return false;
                }

                _position--;
                return true;
            }

            public override void Reset()
            {
                _replacement = string.Empty;
                _position = 0;
            }
        }
    }
}
