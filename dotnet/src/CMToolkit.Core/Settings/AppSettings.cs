using Microsoft.Extensions.Logging;

namespace CMToolkit.Core.Settings;

/// <summary>The <c>log_level</c> setting: the level the log file is filtered at once settings have loaded (SHELL-2).</summary>
public enum SettingsLogLevel
{
    /// <summary><c>DEBUG</c>.</summary>
    Debug,

    /// <summary><c>INFO</c>, the default.</summary>
    Info,

    /// <summary><c>WARNING</c>. Valid in the file, though the Settings tab has no radio for it.</summary>
    Warning,

    /// <summary><c>ERROR</c>.</summary>
    Error,
}

/// <summary>The <c>update_source</c> setting: where the update check looks (GLOSSARY.md, Update Source).</summary>
public enum UpdateSource
{
    /// <summary><c>nexus</c>.</summary>
    Nexus,

    /// <summary><c>github</c>.</summary>
    GitHub,

    /// <summary><c>both</c>.</summary>
    Both,

    /// <summary><c>none</c>: no update check.</summary>
    None,
}

/// <summary>
/// The contents of <c>settings.json</c> (<c>app_settings.AppSettingsDict</c>, SET-1). Immutable: a writer makes a
/// changed copy with <c>with</c> and saves it through the App.
/// </summary>
public sealed record AppSettings
{
    /// <summary><c>log_level</c>.</summary>
    public SettingsLogLevel LogLevel { get; init; } = SettingsLogLevel.Info;

    /// <summary><c>update_source</c>. Its default comes from the Download Source; see <see cref="Defaults"/>.</summary>
    public required UpdateSource UpdateSource { get; init; }

    /// <summary><c>scanner_OverviewIssues</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerOverviewIssues { get; init; } = true;

    /// <summary><c>scanner_Errors</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerErrors { get; init; } = true;

    /// <summary><c>scanner_WrongFormat</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerWrongFormat { get; init; } = true;

    /// <summary><c>scanner_LoosePrevis</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerLoosePrevis { get; init; } = true;

    /// <summary><c>scanner_JunkFiles</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerJunkFiles { get; init; } = true;

    /// <summary><c>scanner_ProblemOverrides</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerProblemOverrides { get; init; } = true;

    /// <summary><c>scanner_RaceSubgraphs</c>. Written but never read by the reference (SET-8, B-1); the port's side pane starts from it (bug #20).</summary>
    public bool ScannerRaceSubgraphs { get; init; } = true;

    /// <summary><c>downgrader_keep_backups</c>.</summary>
    public bool DowngraderKeepBackups { get; init; } = true;

    /// <summary><c>downgrader_delete_deltas</c>.</summary>
    public bool DowngraderDeleteDeltas { get; init; } = true;

    /// <summary>
    /// <c>DEFAULT_SETTINGS</c> (SET-1): <c>INFO</c>, every bool true, and <c>update_source</c> seeded from the archive's
    /// Download Source.
    /// </summary>
    public static AppSettings Defaults(DownloadSource downloadSource) => new()
    {
        UpdateSource = downloadSource switch
        {
            DownloadSource.Nexus => UpdateSource.Nexus,
            DownloadSource.GitHub => UpdateSource.GitHub,
            _ => throw new ArgumentOutOfRangeException(nameof(downloadSource), downloadSource, null),
        },
    };
}

/// <summary>Conversions of <see cref="SettingsLogLevel"/>.</summary>
public static class SettingsLogLevelExtensions
{
    /// <summary>The <see cref="LogLevel"/> the log file is filtered at for this setting (<c>logger.setLevel</c>).</summary>
    public static LogLevel ToLogLevel(this SettingsLogLevel level) => level switch
    {
        SettingsLogLevel.Debug => LogLevel.Debug,
        SettingsLogLevel.Info => LogLevel.Information,
        SettingsLogLevel.Warning => LogLevel.Warning,
        SettingsLogLevel.Error => LogLevel.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
