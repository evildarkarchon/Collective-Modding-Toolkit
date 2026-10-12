using CMToolkit.Core;
using CMToolkit.Core.Settings;
using Microsoft.Extensions.Logging;

namespace CMToolkit.App.Runtime;

/// <summary>
/// The settings in effect for this session, shared by everything that reads or writes them: the reference's single
/// <c>AppSettings</c> instance (<c>cmc.settings</c>). Core owns the file format; the App is the only writer, through
/// <see cref="Save"/> (the Settings tab, the Scanner and the Downgrader; SET-7).
/// </summary>
public sealed class SettingsStore
{
    private readonly string? _path;
    private readonly ILogger _logger;
    private volatile AppSettings _current;

    private SettingsStore(string? path, AppSettings current, ILogger logger)
    {
        _path = path;
        _current = current;
        _logger = logger;
    }

    /// <summary>The settings in effect.</summary>
    public AppSettings Current => _current;

    /// <summary>
    /// Loads <paramref name="path"/> (SET-1, SET-3..SET-6), which may resave it, and keeps it as the file
    /// <see cref="Save"/> writes. Never throws.
    /// </summary>
    /// <param name="path">The settings file; startup passes the CWD-relative <see cref="WorkingDirectoryFiles.Settings"/>.</param>
    /// <param name="downloadSource">Seeds the default <c>update_source</c>.</param>
    /// <param name="logger">The app log.</param>
    public static SettingsStore Load(string path, DownloadSource downloadSource, ILogger logger)
        => new(path, SettingsFile.Load(path, downloadSource, logger), logger);

    /// <summary>
    /// A store backed by no file, whose <see cref="Save"/> only updates <see cref="Current"/>: for view models built
    /// without a settings file, such as in tests.
    /// </summary>
    public static SettingsStore Unsaved(AppSettings settings) => new(null, settings, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

    /// <summary>
    /// Makes <paramref name="settings"/> current and writes the file (<c>AppSettings.save</c>, SET-6). A failed write is
    /// logged and swallowed; the new settings stay in effect for the session either way.
    /// </summary>
    public void Save(AppSettings settings)
    {
        _current = settings;
        if (_path is not null)
        {
            SettingsFile.Save(_path, settings, _logger);
        }
    }
}
