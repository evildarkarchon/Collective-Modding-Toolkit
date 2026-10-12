using Avalonia.Controls;
using Avalonia.LogicalTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;

namespace CMToolkit.Tests.Support;

/// <summary>Shows one real tab the way the app does: the only page of a main window, loaded on open.</summary>
public static class TabHost
{
    /// <summary>
    /// Shows a main window holding just <paramref name="page"/>, waits until it has loaded and built, and returns the
    /// window. The page sits in the real tab content area, so layout matches the running app.
    /// </summary>
    public static MainWindow Show(AppRuntime runtime, TabPage page)
    {
        var window = new MainWindow(runtime, [page]);
        window.Show();
        DispatcherWait.Until(() => Lazy(window).Content is { } content && content is not TextBlock);
        window.UpdateLayout();
        return window;
    }

    /// <summary>What the shown page built.</summary>
    public static Control Content(MainWindow window) => (Control)Lazy(window).Content!;

    private static LazyTab Lazy(MainWindow window)
        => (LazyTab)window.GetLogicalDescendants().OfType<TabItem>().Single().Content!;
}
