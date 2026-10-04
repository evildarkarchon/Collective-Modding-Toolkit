using CMToolkit.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CMToolkit.App.ViewModels;

/// <summary>The main window's state: its title and the results of the Core calls made at startup.</summary>
public sealed class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(DownloadSourceLookup downloadSource)
    {
        DownloadSource = downloadSource;
    }

    /// <summary><c>Collective Modding Toolkit v&lt;version&gt;</c> (SHELL-6).</summary>
    public string Title => AppInfo.WindowTitle;

    /// <summary>
    /// The archive's baked-in Download Source. Nothing displays it yet; the settings slice uses it to seed the
    /// default Update Source (SET-1), and the logging slice reports its outcome.
    /// </summary>
    public DownloadSourceLookup DownloadSource { get; }

    /// <summary>Runs the startup Core calls against <paramref name="appDirectory"/>, the folder holding the exe.</summary>
    public static MainWindowViewModel Load(string appDirectory) => new(DownloadSourceFile.Read(appDirectory));
}
