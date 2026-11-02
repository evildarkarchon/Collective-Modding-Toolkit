using Avalonia.Controls;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using Microsoft.Extensions.Logging.Abstractions;

namespace CMToolkit.App.Views;

/// <summary>
/// The main window: the tab strip of lazily loaded tabs, and closing on Escape or a user close.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppRuntime _runtime;
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
        _runtime = runtime;
        foreach (var page in pages)
        {
            Tabs.Items.Add(new TabItem { Header = page.Header, Content = new LazyTab(page, runtime) });
        }

        // Escape and a user close both go through RequestClose, so during a blocking Operation the input gate defers
        // them alike and honours them once it ends.
        runtime.Input.Attach(this, onEscape: RequestClose, onCloseRequest: RequestClose);

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

    /// <summary>The current tab's page (<c>CMChecker.current_tab</c>): the last one selected, or null before the first.</summary>
    public TabPage? CurrentPage => _current?.Page;

    /// <summary>
    /// The update banner in row 0, above the tabs (SHELL-7, UPD-2), or null for none. The rows share the fixed
    /// 760x450 client area, so a banner takes its height from the tabs, as Tk's row 1 (weight 1) gave it up.
    /// </summary>
    public Control? UpdateBanner
    {
        get => UpdateBannerHost.Content as Control;
        set => UpdateBannerHost.Content = value;
    }

    /// <summary>
    /// Closes the window, for a user close and for Escape. The main window has no close guard: during a blocking
    /// Operation the input gate defers both until it ends, and nothing else holds the window open.
    /// </summary>
    public void RequestClose() => Close();

    /// <summary><c>CMChecker.on_tab_changed</c>: switch from the old tab, then select (and maybe load) the new one.</summary>
    private void OnTabChanged()
    {
        try
        {
            _current?.Deselect();
        }
        catch (Exception ex)
        {
            // The reference's switch_from raised inside on_tab_changed, before current_tab moved on and before the new
            // tab loaded: the Error Window shows it and this tab stays current. (Bug #87, the Scanner's switch_from on
            // a tab that never built, was how the reference got here; the port fixes it, so no tab throws here today.)
            _runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
            return;
        }

        _current = (Tabs.SelectedItem as TabItem)?.Content as LazyTab;
        if (_current is not null)
        {
            // Handle's task never faults, so it is safe to discard. A failed load's input block is already released
            // when Handle sees the exception, but deferred input replays from a posted job, and Handle reports
            // synchronously, so the Error Window is up first.
            _ = _runtime.Errors.Handle(_current.SelectAsync);
        }
    }
}
