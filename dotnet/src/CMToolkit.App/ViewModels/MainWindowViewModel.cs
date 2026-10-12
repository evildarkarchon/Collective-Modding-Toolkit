using CMToolkit.App.Runtime;
using CMToolkit.Core;
using CMToolkit.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CMToolkit.App.ViewModels;

/// <summary>The main window's state: its title and the results of the Core calls made at startup.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// A view model whose settings are the defaults for <paramref name="downloadSource"/>, backed by no file (see
    /// <see cref="SettingsStore.Unsaved"/>).
    /// </summary>
    public MainWindowViewModel(DownloadSourceLookup downloadSource)
        : this(downloadSource, SettingsStore.Unsaved(AppSettings.Defaults(downloadSource.Source)))
    {
    }

    /// <summary>A view model over the settings loaded at startup.</summary>
    public MainWindowViewModel(DownloadSourceLookup downloadSource, SettingsStore settings)
    {
        DownloadSource = downloadSource;
        Settings = settings;
    }

    /// <summary><c>Collective Modding Toolkit v&lt;version&gt;</c> (SHELL-6).</summary>
    public string Title => AppInfo.WindowTitle;

    /// <summary>
    /// The archive's baked-in Download Source. Nothing displays it; it seeded the default Update Source (SET-1). The
    /// reference read it before logging was configured, so its lines never reached <c>cm-toolkit.log</c> (bug #77);
    /// <see cref="Load(IHostEnvironment, LogFile, string)"/> logs its outcome there (ADR-0006).
    /// </summary>
    public DownloadSourceLookup DownloadSource { get; }

    /// <summary>The settings in effect, which the Settings tab, the Scanner and the Downgrader read and save (SET-7).</summary>
    public SettingsStore Settings { get; }

    /// <summary>Runs the startup Core calls against <paramref name="appDirectory"/>, the folder holding the exe.</summary>
    public static MainWindowViewModel Load(string appDirectory) => new(DownloadSourceFile.Read(appDirectory));

    /// <summary>Runs the startup Core calls against <paramref name="host"/>'s exe folder.</summary>
    public static MainWindowViewModel Load(IHostEnvironment host) => Load(host.AppDirectory);

    /// <summary>
    /// The startup Core calls in <c>main.py</c>'s order, once <paramref name="log"/> is open and its start lines written:
    /// the Download Source lookup and its log line (bug #77, fixed), then the settings load (still filtered at INFO), then <c>log_level</c> applied to the
    /// log (SHELL-2). Runs off the UI thread at startup.
    /// </summary>
    /// <param name="host">The machine; its exe folder holds the Download Source file.</param>
    /// <param name="log">The open log file, whose level is set from the loaded settings.</param>
    /// <param name="settingsPath">The settings file; the CWD-relative <see cref="WorkingDirectoryFiles.Settings"/> by default.</param>
    public static MainWindowViewModel Load(IHostEnvironment host, LogFile log, string settingsPath = WorkingDirectoryFiles.Settings)
    {
        var downloadSource = DownloadSourceFile.Read(host.AppDirectory);
        // Bug #77, fixed (ADR-0006): the reference logged this before its log existed. Logged here, after the start
        // lines and while the log still filters at INFO, as main.py would have with basicConfig ahead of the import.
        DownloadSourceFile.LogOutcome(downloadSource, log);
        var settings = SettingsStore.Load(settingsPath, downloadSource.Source, log);
        log.MinimumLevel = settings.Current.LogLevel.ToLogLevel();
        return new MainWindowViewModel(downloadSource, settings);
    }
}
