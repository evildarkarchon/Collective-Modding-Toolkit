using Avalonia.Controls;

namespace CMToolkit.App.Tabs;

/// <summary>
/// One main-window tab (<c>helpers.CMCTabFrame</c>): what it loads, what it builds, and what it does when it is
/// switched to and from. <see cref="LazyTab"/> drives the lifecycle (SHELL-8).
/// </summary>
public abstract class TabPage
{
    /// <param name="header">The tab strip text.</param>
    /// <param name="logName">The reference's class name, which the <c>Switch Tab</c> and <c>Load Tab</c> lines show.</param>
    protected TabPage(string header, string logName)
    {
        Header = header;
        LogName = logName;
    }

    /// <summary>The tab strip text.</summary>
    public string Header { get; }

    /// <summary>The reference's class name, such as <c>OverviewTab</c>, for the log lines.</summary>
    public string LogName { get; }

    /// <summary>The text shown while the tab loads (<c>loading_text</c>); <see langword="null"/> shows an empty label.</summary>
    public virtual string? LoadingText => null;

    /// <summary>
    /// Why the load failed (<c>loading_error</c>), set by <see cref="LoadAsync"/> before it returns
    /// <see langword="false"/>. <see langword="null"/> shows <c>Failed to load tab.</c> and logs <c>None</c>.
    /// </summary>
    public string? LoadingError { get; protected set; }

    /// <summary>
    /// Loads the tab's data (<c>_load</c>), on the UI thread with input blocked; Core calls inside it go through the
    /// background-operation runner. Called at most once.
    /// </summary>
    /// <returns>Whether the load succeeded. An exception, like <see langword="false"/>, leaves the tab unloaded for
    /// good, but keeps the loading text up instead of the error.</returns>
    protected internal virtual Task<bool> LoadAsync() => Task.FromResult(true);

    /// <summary>Builds the tab's content after a successful load (<c>_build_gui</c>). Called at most once.</summary>
    protected internal abstract Control BuildContent();

    /// <summary>Runs each time the loaded tab is selected, including right after it is built (<c>_switch_to</c>).</summary>
    protected internal virtual void SwitchTo()
    {
    }

    /// <summary>Runs each time another tab is selected while this one was current (<c>switch_from</c>), loaded or not.</summary>
    protected internal virtual void SwitchFrom()
    {
    }
}
