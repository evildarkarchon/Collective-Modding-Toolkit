using Avalonia.Controls;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.App.Views;

/// <summary>
/// The main window (<c>cm_checker.CMChecker</c>): the tab strip of lazily loaded tabs, the close guard and Escape.
/// </summary>
public partial class MainWindow : Window
{
    private LazyTab? _current;
    private bool _opened;

    /// <summary>
    /// For the XAML previewer and Avalonia's runtime loader only (AVLN3001 asks for a public parameterless
    /// constructor): the placeholder tabs on a runtime with no log. The app uses the other constructor.
    /// </summary>
    public MainWindow()
        : this(new AppRuntime(NullLogger.Instance), PlaceholderTab.Shell())
    {
    }

    /// <param name="runtime">The app runtime; the window attaches itself to its input block.</param>
    /// <param name="pages">The tabs, in strip order.</param>
    public MainWindow(AppRuntime runtime, IReadOnlyList<TabPage> pages)
    {
        InitializeComponent();
        foreach (var page in pages)
        {
            Tabs.Items.Add(new TabItem { Header = page.Header, Content = new LazyTab(page, runtime) });
        }

        // Escape calls root.destroy() directly, bypassing the close guard (SHELL-10, B-12); a user close goes through
        // it (SHELL-9).
        runtime.Input.Attach(this, onEscape: Close, onCloseRequest: RequestClose);

        Tabs.SelectionChanged += (_, e) =>
        {
            // SelectionChanged bubbles, so a list inside a tab would raise it here too.
            if (ReferenceEquals(e.Source, Tabs) && _opened)
            {
                OnTabChanged();
            }
        };

        // <<NotebookTabChanged>> first fires once the window is up, which is when the initially selected Overview
        // tab loads.
        Opened += (_, _) =>
        {
            _opened = true;
            OnTabChanged();
        };
    }

    /// <summary>
    /// The close guard (<c>CMChecker.processing_data</c>): while set, a user close is ignored. Nothing sets it, in the
    /// reference or here, so in practice closing always works (SHELL-9).
    /// </summary>
    public bool ProcessingData { get; set; }

    /// <summary>Closes the window unless <see cref="ProcessingData"/> is set (<c>CMChecker.on_close</c>).</summary>
    public void RequestClose()
    {
        if (!ProcessingData)
        {
            Close();
        }
    }

    /// <summary><c>CMChecker.on_tab_changed</c>: switch from the old tab, then select (and maybe load) the new one.</summary>
    private void OnTabChanged()
    {
        _current?.Deselect();
        _current = (Tabs.SelectedItem as TabItem)?.Content as LazyTab;
        // SelectAsync reports its own exceptions, so the task is never faulted.
        _ = _current?.SelectAsync();
    }
}
