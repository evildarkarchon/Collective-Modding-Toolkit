using CMToolkit.App;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Logging;

namespace CMToolkit.Tests.App;

/// <summary>
/// The start of <c>main.py</c> as the App runs it: the log opens with its start lines, settings load while the log
/// still filters at INFO, then <c>log_level</c> applies (SHELL-1, SHELL-2). Expected log text is what the Reference
/// Implementation wrote for the same files.
/// </summary>
public sealed class StartupSettingsTests
{
    private sealed class AppDirectoryHost(string appDirectory) : IHostEnvironment
    {
        public string AppDirectory => appDirectory;
    }

    [Fact]
    [Trait("Parity", "SHELL-2")]
    [Trait("Parity", "LOG-2")]
    public void Settings_load_logs_at_info_and_then_log_level_applies()
    {
        using var dir = new TempDirectory();
        dir.WriteBytes(Path.Combine("App", "assets", "download-source.txt"), "github"u8.ToArray());
        var settingsPath = dir.WriteBytes(
            "settings.json", """{"log_level": "DEBUG", "theme": "dark", "scanner_Errors": 1}"""u8.ToArray());
        var logPath = Path.Combine(dir.Path, "cm-toolkit.log");

        using (var log = AppStartup.OpenLog(logPath, new DateTime(2026, 10, 12, 9, 30, 0)))
        {
            var vm = MainWindowViewModel.Load(new AppDirectoryHost(Path.Combine(dir.Path, "App")), log, settingsPath);

            Assert.Equal(SettingsLogLevel.Debug, vm.Settings.Current.LogLevel);
            Assert.Equal(UpdateSource.GitHub, vm.Settings.Current.UpdateSource);
            Assert.Equal(LogLevel.Debug, log.MinimumLevel);
            log.Write(LogLevel.Debug, "Switch Tab : Overview");
        }

        // The DEBUG lines of the load itself ("is correct type", "Saving") were filtered at INFO; the ones after it
        // pass at DEBUG. The SET-2 Download Source line is absent, as it was from the reference's log (issue #77).
        Assert.Equal(
            "INFO : ----------------------------------------------\r\n"
            + "INFO : Starting Collective Modding Toolkit v0.6.2-dev\r\n"
            + "INFO : 2026-10-12 09:30\r\n"
            + "INFO : Settings : Adding new settings to JSON: update_source, scanner_OverviewIssues, scanner_WrongFormat, "
            + "scanner_LoosePrevis, scanner_JunkFiles, scanner_ProblemOverrides, scanner_RaceSubgraphs, "
            + "downgrader_keep_backups, downgrader_delete_deltas\r\n"
            + "ERROR : Settings : Unknown setting 'theme' will be removed.\r\n"
            + "ERROR : Settings : 'scanner_Errors' has invalid type (int) '1'. Reset to 'True'\r\n"
            + "DEBUG : Switch Tab : Overview\r\n",
            File.ReadAllText(logPath));
    }

    [Fact]
    [Trait("Parity", "SHELL-2")]
    public void A_higher_log_level_drops_later_info_lines()
    {
        using var dir = new TempDirectory();
        dir.WriteBytes(Path.Combine("App", "assets", "download-source.txt"), "nexus"u8.ToArray());
        var settingsPath = dir.WriteBytes("settings.json", """{"log_level": "ERROR"}"""u8.ToArray());
        var logPath = Path.Combine(dir.Path, "cm-toolkit.log");

        using (var log = AppStartup.OpenLog(logPath, DateTime.Now))
        {
            MainWindowViewModel.Load(new AppDirectoryHost(Path.Combine(dir.Path, "App")), log, settingsPath);
            log.Write(LogLevel.Information, "Load Tab : OverviewTab");
            log.Write(LogLevel.Error, "StdErr : later");
        }

        var lines = File.ReadAllLines(logPath);
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("INFO : Settings : Adding new settings to JSON: update_source, ", lines[3], StringComparison.Ordinal);
        Assert.Equal("ERROR : StdErr : later", lines[4]);
    }

    [Fact]
    [Trait("Parity", "SET-2")]
    [Trait("Parity", "SHELL-1")]
    public void An_invalid_download_source_leaves_no_line_in_the_log_as_in_the_reference()
    {
        using var dir = new TempDirectory();
        dir.WriteBytes(Path.Combine("App", "assets", "download-source.txt"), "bogus"u8.ToArray());
        var settingsPath = Path.Combine(dir.Path, "settings.json");
        var logPath = Path.Combine(dir.Path, "cm-toolkit.log");

        using (var log = AppStartup.OpenLog(logPath, DateTime.Now))
        {
            var vm = MainWindowViewModel.Load(new AppDirectoryHost(Path.Combine(dir.Path, "App")), log, settingsPath);
            Assert.Equal(DownloadSourceOutcome.Invalid, vm.DownloadSource.Outcome);
            Assert.Equal(UpdateSource.Nexus, vm.Settings.Current.UpdateSource);
        }

        // Bug #77: the reference logged it before logging was configured, so it never reached the file.
        var text = File.ReadAllText(logPath);
        Assert.DoesNotContain("download source", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("INFO : Settings : settings.json not found; using defaults.\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Parity", "SET-6")]
    public void Saving_through_the_store_rewrites_the_file_and_keeps_the_new_settings()
    {
        using var dir = new TempDirectory();
        var settingsPath = Path.Combine(dir.Path, "settings.json");
        var logger = new RecordingLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.Nexus, logger);

        store.Save(store.Current with { DowngraderKeepBackups = false, LogLevel = SettingsLogLevel.Warning });

        Assert.False(store.Current.DowngraderKeepBackups);
        Assert.Equal(
            "{\r\n\t\"log_level\": \"WARNING\",\r\n\t\"update_source\": \"nexus\",\r\n\t\"scanner_OverviewIssues\": true,\r\n"
            + "\t\"scanner_Errors\": true,\r\n\t\"scanner_WrongFormat\": true,\r\n\t\"scanner_LoosePrevis\": true,\r\n"
            + "\t\"scanner_JunkFiles\": true,\r\n\t\"scanner_ProblemOverrides\": true,\r\n\t\"scanner_RaceSubgraphs\": true,\r\n"
            + "\t\"downgrader_keep_backups\": false,\r\n\t\"downgrader_delete_deltas\": true\r\n}\r\n",
            File.ReadAllText(settingsPath));
        Assert.Equal("Settings : Saving settings.json", logger.Records[^1].Message);
    }

    [Fact]
    [Trait("Parity", "SET-6")]
    public void A_failed_save_is_logged_and_swallowed_and_the_new_settings_stay_in_effect()
    {
        using var dir = new TempDirectory();
        var settingsPath = dir.WriteBytes("settings.json", "{}"u8.ToArray());
        var logger = new RecordingLogger();
        var store = SettingsStore.Load(settingsPath, DownloadSource.GitHub, logger);
        File.SetAttributes(settingsPath, FileAttributes.ReadOnly);
        try
        {
            store.Save(store.Current with { UpdateSource = UpdateSource.None });

            Assert.Equal(UpdateSource.None, store.Current.UpdateSource);
            var failure = logger.Records[^1];
            Assert.Equal((LogLevel.Error, "Settings : Failed to save settings.json"), (failure.Level, failure.Message));
            Assert.IsType<UnauthorizedAccessException>(failure.Exception);
        }
        finally
        {
            File.SetAttributes(settingsPath, FileAttributes.Normal);
        }
    }
}
