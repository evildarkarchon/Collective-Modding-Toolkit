using System.Text;
using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Tests.Parity;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>cm-toolkit.log</c> as <c>logging.basicConfig</c> wrote it (SHELL-1): append mode, <c>LEVEL : message</c> with
/// Python's level names, CRLF, the ANSI code page with <c>backslashreplace</c>, and the INFO level until settings load
/// (SHELL-2). Expected bytes are the ones the Reference Implementation wrote in the same situations.
/// </summary>
public sealed class LogFileTests
{
    private const int Windows1252 = 1252;
    private const int Utf8 = 65001;

    [Fact]
    [Trait("Parity", "SHELL-1"), Deviation(71, "A deliberate change at the Cutover, not a reference bug: the version is 0.7.0, the first C# release, where the reference ran as 0.6.2-dev.")]
    public void Startup_appends_the_three_start_lines_at_info_with_crlf()
    {
        using var dir = new TempDirectory();
        var path = dir.WriteBytes("cm-toolkit.log", "INFO : earlier run\r\n"u8.ToArray());

        using (var log = AppStartup.OpenLog(path, new DateTime(2026, 3, 4, 5, 6, 59)))
        {
            Assert.Equal(LogLevel.Information, log.MinimumLevel);
        }

        Assert.Equal(
            "INFO : earlier run\r\n"
            + "INFO : ------------------------------------------\r\n"
            + "INFO : Starting Collective Modding Toolkit v0.7.0\r\n"
            + "INFO : 2026-03-04 05:06\r\n",
            File.ReadAllText(path));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void The_dash_rule_is_as_long_as_the_start_message()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");

        using (var log = AppStartup.OpenLog(path, DateTime.Now))
        {
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal($"Starting {AppInfo.Name} v{AppInfo.Version}".Length, lines[0].Length - "INFO : ".Length);
        Assert.Matches("^INFO : -+$", lines[0]);
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void A_log_that_cannot_be_opened_throws_so_startup_fails()
    {
        using var dir = new TempDirectory();
        // A folder where the file should be: Python's open() raises PermissionError here too.
        Directory.CreateDirectory(Path.Combine(dir.Path, "cm-toolkit.log"));

        Assert.Throws<UnauthorizedAccessException>(() => AppStartup.OpenLog(Path.Combine(dir.Path, "cm-toolkit.log"), DateTime.Now));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void Lines_use_pythons_level_names()
    {
        var text = Write(Utf8, LogLevel.Trace, log =>
        {
            log.Write(LogLevel.Trace, "t");
            log.Write(LogLevel.Debug, "d");
            log.Write(LogLevel.Information, "i");
            log.Write(LogLevel.Warning, "w");
            log.Write(LogLevel.Error, "e");
            log.Write(LogLevel.Critical, "c");
        });

        // Python has nothing below DEBUG, so Trace shares its name.
        Assert.Equal("DEBUG : t\r\nDEBUG : d\r\nINFO : i\r\nWARNING : w\r\nERROR : e\r\nCRITICAL : c\r\n", text);
    }

    [Fact]
    [Trait("Parity", "SHELL-2")]
    public void Lines_below_the_minimum_level_are_dropped_until_it_is_lowered()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");
        using (var log = LogFile.Open(path, Utf8))
        {
            log.Write(LogLevel.Debug, "Settings : 'log_level' is correct type (str)");
            log.Write(LogLevel.Information, "Settings : Adding new settings to JSON: scanner_Errors");
            log.MinimumLevel = LogLevel.Debug;
            log.Write(LogLevel.Debug, "Switch Tab : Overview");
            log.MinimumLevel = LogLevel.Error;
            log.Write(LogLevel.Warning, "dropped");
            log.Write(LogLevel.Error, "kept");
        }

        Assert.Equal(
            "INFO : Settings : Adding new settings to JSON: scanner_Errors\r\nDEBUG : Switch Tab : Overview\r\nERROR : kept\r\n",
            File.ReadAllText(path));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void An_exception_follows_its_message_on_the_next_lines_with_crlf_only()
    {
        var text = Write(Utf8, LogLevel.Information, log =>
        {
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (InvalidOperationException ex)
            {
                log.Write(LogLevel.Error, "Settings : Failed to save settings.json", ex);
            }
        });

        Assert.StartsWith(
            "ERROR : Settings : Failed to save settings.json\r\nSystem.InvalidOperationException: boom\r\n   at ",
            text,
            StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\r", text, StringComparison.Ordinal);
        Assert.DoesNotMatch("[^\r]\n", text);
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void Newlines_inside_a_message_are_written_as_crlf()
    {
        var text = Write(Utf8, LogLevel.Information, log => log.Write(LogLevel.Error, "StdErr : header\nline two"));

        Assert.Equal("ERROR : StdErr : header\r\nline two\r\n", text);
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void Characters_the_code_page_lacks_are_backslash_escaped()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");
        using (var log = LogFile.Open(path, Windows1252))
        {
            log.Write(LogLevel.Information, "caf\u00e9 \u20ac \u4e2d \U0001F600 \udc80 \u0100");
        }

        // Python: "café € 中 😀 \udc80 Ā".encode("cp1252", "backslashreplace").
        Assert.Equal(
            Encoding.Latin1.GetBytes("INFO : caf\u00e9 \u0080 \\u4e2d \\U0001f600 \\udc80 \\u0100\r\n"),
            File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void Utf8_only_escapes_lone_surrogates()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");
        using (var log = LogFile.Open(path, Utf8))
        {
            log.Write(LogLevel.Debug, "x");
            log.Write(LogLevel.Information, "\u4e2d \U0001F600 \udc80 caf\u00e9");
        }

        // The bytes the Reference Implementation wrote with the ANSI code page set to UTF-8 (65001); no BOM.
        Assert.Equal(
            "INFO : \u4e2d \U0001F600 \\udc80 caf\u00e9\r\n"u8.ToArray(),
            File.ReadAllBytes(path));
    }

    [Fact]
    [Trait("Parity", "SHELL-1")]
    public void Lines_logged_after_dispose_are_dropped()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");
        var log = LogFile.Open(path, Utf8);
        log.Dispose();

        log.Write(LogLevel.Error, "late");

        Assert.Equal(string.Empty, File.ReadAllText(path));
    }

    /// <summary>Opens a fresh log in <paramref name="codePage"/> at <paramref name="level"/>, runs <paramref name="act"/>, and returns the file as UTF-8 text.</summary>
    private static string Write(int codePage, LogLevel level, Action<LogFile> act)
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "cm-toolkit.log");
        using (var log = LogFile.Open(path, codePage))
        {
            log.MinimumLevel = level;
            act(log);
        }

        return File.ReadAllText(path);
    }
}
