using System.Text;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.Core;

/// <summary>
/// Settings-file behaviour the recorded scenarios can't carry: text a golden can't hold (lone surrogates), inputs that
/// would crash rather than reset, and the writer's exact output (T-10). Expected values are what the Reference
/// Implementation produced for the same input.
/// </summary>
public sealed class SettingsFileTests
{
    [Fact]
    [Trait("Parity", "T-10")]
    [Trait("Parity", "SET-1")]
    public void Format_is_json_dump_with_a_tab_indent_and_a_final_newline()
    {
        var settings = AppSettings.Defaults(DownloadSource.Nexus) with
        {
            LogLevel = SettingsLogLevel.Error,
            UpdateSource = UpdateSource.Both,
            ScannerJunkFiles = false,
        };

        // json.dump(settings, f, indent="\t"); f.write("\n"), before text mode turns LF into CRLF.
        Assert.Equal(
            "{\n\t\"log_level\": \"ERROR\",\n\t\"update_source\": \"both\",\n\t\"scanner_OverviewIssues\": true,\n"
            + "\t\"scanner_Errors\": true,\n\t\"scanner_WrongFormat\": true,\n\t\"scanner_LoosePrevis\": true,\n"
            + "\t\"scanner_JunkFiles\": false,\n\t\"scanner_ProblemOverrides\": true,\n\t\"scanner_RaceSubgraphs\": true,\n"
            + "\t\"downgrader_keep_backups\": true,\n\t\"downgrader_delete_deltas\": true\n}\n",
            SettingsFile.Format(settings));
    }

    [Fact]
    [Trait("Parity", "SET-1")]
    public void Defaults_seed_the_update_source_from_the_download_source()
    {
        Assert.Equal(UpdateSource.GitHub, AppSettings.Defaults(DownloadSource.GitHub).UpdateSource);
        Assert.Equal(UpdateSource.Nexus, AppSettings.Defaults(DownloadSource.Nexus).UpdateSource);
        Assert.Equal(SettingsLogLevel.Info, AppSettings.Defaults(DownloadSource.Nexus).LogLevel);
    }

    [Fact]
    [Trait("Parity", "SET-5")]
    public void A_lone_surrogate_key_is_logged_with_the_surrogate()
    {
        // A golden can't hold this: System.Text.Json refuses a lone surrogate escape.
        var (_, records, _) = Load("""{"\udc80\ud83d": 1, "😀\ud800x": 2}""");

        Assert.Equal("Settings : Unknown setting '\udc80\ud83d' will be removed.", records[1].Message);
        Assert.Equal("Settings : Unknown setting '\U0001F600\ud800x' will be removed.", records[2].Message);
    }

    [Fact]
    [Trait("Parity", "SET-5")]
    public void Lone_surrogates_inside_a_container_are_escaped_by_repr()
    {
        var (_, records, _) = Load("""{"log_level": ["\udc80", "😀"]}""");

        // Python: str(["\udc80", "\U0001f600"]) == "['\\udc80', '😀']".
        Assert.Equal(
            "Settings : 'log_level' has invalid value '['\\udc80', '\U0001F600']'. Reset to 'INFO'",
            records.Single(r => r.Level == LogLevel.Error).Message);
    }

    [Fact]
    [Trait("Parity", "SET-4")]
    public void Absurdly_deep_nesting_resets_instead_of_overflowing_the_stack()
    {
        var (settings, records, _) = Load("{\"log_level\": " + new string('[', 100_000) + new string(']', 100_000) + "}");

        Assert.Equal(SettingsLogLevel.Info, settings.LogLevel);
        Assert.Equal("Settings : Failed to load settings.json. Settings will be reset.", records[0].Message);
        Assert.NotNull(records[0].Exception);
    }

    [Theory]
    [Trait("Parity", "SET-5")]
    [InlineData("1.0", "1.0")]
    [InlineData("123456789.125", "123456789.125")]
    [InlineData("1e22", "1e+22")]
    [InlineData("1.7976931348623157e308", "1.7976931348623157e+308")]
    [InlineData("5e-324", "5e-324")]
    [InlineData("0.001", "0.001")]
    [InlineData("-1.5e-5", "-1.5e-05")]
    [InlineData("9999999999999998.0", "9999999999999998.0")]
    [InlineData("12345678901234567.0", "1.2345678901234568e+16")]
    [InlineData("0.1e1", "1.0")]
    [InlineData("-0e0", "-0.0")]
    public void Floats_are_logged_as_python_str(string json, string python)
    {
        var (_, records, _) = Load($$"""{"scanner_Errors": {{json}}}""");

        Assert.Equal(
            $"Settings : 'scanner_Errors' has invalid type (float) '{python}'. Reset to 'True'",
            records.Single(r => r.Level == LogLevel.Error).Message);
    }

    [Fact]
    [Trait("Parity", "SET-3")]
    public void A_missing_file_is_written_with_crlf_line_endings()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "settings.json");

        SettingsFile.Load(path, DownloadSource.GitHub, new RecordingLogger());

        var expected = SettingsFile.Format(AppSettings.Defaults(DownloadSource.GitHub)).Replace("\n", "\r\n", StringComparison.Ordinal);
        Assert.Equal(Encoding.ASCII.GetBytes(expected), File.ReadAllBytes(path));
    }

    /// <summary>Loads <paramref name="json"/> from a temp <c>settings.json</c> with a GitHub Download Source.</summary>
    private static (AppSettings Settings, IReadOnlyList<LogRecord> Records, string FileText) Load(string json)
    {
        using var dir = new TempDirectory();
        var path = dir.WriteBytes("settings.json", Encoding.UTF8.GetBytes(json));
        var logger = new RecordingLogger();
        var settings = SettingsFile.Load(path, DownloadSource.GitHub, logger);
        return (settings, logger.Records, File.ReadAllText(path));
    }
}
