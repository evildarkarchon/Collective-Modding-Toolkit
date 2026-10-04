using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CMToolkit.App.ViewModels;
using CMToolkit.App.Views;
using CMToolkit.Core;

namespace CMToolkit.Tests.App;

/// <summary>The main window shell: <c>cm_checker.CMChecker.setup_window</c> and <c>utils.set_theme</c>.</summary>
public sealed class MainWindowTests
{
    private static MainWindow ShowShell()
    {
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(
                new DownloadSourceLookup(DownloadSource.GitHub, DownloadSourceOutcome.Valid, "github", null)),
        };
        window.Show();
        return window;
    }

    private static TabControl Tabs(Window window) => window.GetLogicalDescendants().OfType<TabControl>().Single();

    [AvaloniaFact]
    [Trait("Parity", "SHELL-6")]
    public void The_window_has_the_reference_title_and_icon()
    {
        var window = ShowShell();

        Assert.Equal("Collective Modding Toolkit v0.6.2-dev", window.Title);
        Assert.NotNull(window.Icon);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-6")]
    public void The_window_is_a_fixed_760_by_450_client_area()
    {
        var window = ShowShell();

        Assert.Equal(new Size(760, 450), window.ClientSize);
        Assert.False(window.CanResize);
        Assert.False(window.CanMaximize);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-6")]
    public void The_window_opens_at_the_reference_centring_formula_on_the_primary_screen()
    {
        var window = new MainWindow();
        var primary = window.Screens.Primary!;

        Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
        Assert.Equal(ShellPlacement.TkCentredOrigin(primary.Bounds, primary.Scaling), window.Position);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-7")]
    public void The_six_tabs_are_in_the_reference_order()
    {
        var tabs = Tabs(ShowShell());

        Assert.Equal(
            ["Overview", "F4SE", "Scanner", "Tools", "Settings", "About"],
            tabs.Items.Cast<TabItem>().Select(t => t.Header as string));
        Assert.Equal(0, tabs.SelectedIndex);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-7")]
    public void The_tab_strip_fills_the_second_grid_row_under_an_auto_height_banner_row()
    {
        var tabs = Tabs(ShowShell());
        var grid = Assert.IsType<Grid>(tabs.Parent);

        Assert.Equal(1, Grid.GetRow(tabs));
        Assert.True(grid.RowDefinitions[0].Height.IsAuto);
        Assert.True(grid.RowDefinitions[1].Height.IsStar);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-14")]
    public void The_app_runs_fluent_dark()
    {
        var window = ShowShell();

        Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-14")]
    public void Tabs_use_the_notebook_control_theme_with_cascadia_at_the_tab_font_and_no_focus_ring()
    {
        var tab = Tabs(ShowShell()).Items.Cast<TabItem>().First();

        // Our TabItem template is a single PART_LayoutRoot border; Fluent's pivot template has a selection pipe.
        Assert.Contains(tab.GetVisualDescendants(), v => v is Border { Name: "PART_LayoutRoot" });
        Assert.DoesNotContain(tab.GetVisualDescendants(), v => v.Name == "PART_SelectedPipe");
        Assert.Null(tab.FocusAdorner);

        // style.configure("Tab", font=FONT): Cascadia Mono 12 pt, which Tk renders at 16 px.
        var header = tab.GetVisualDescendants().OfType<TextBlock>().Single();
        Assert.Equal("Cascadia Mono", header.FontFamily.Name);
        Assert.Equal(16, header.FontSize);
    }

    [AvaloniaFact]
    [Trait("Parity", "SHELL-14")]
    public void Groupboxes_use_the_labelframe_control_theme_with_a_4px_gutter()
    {
        var box = new GroupBox { Header = "Binaries", Content = new TextBlock { Text = "x" } };
        var window = new Window { Content = box };
        window.Show();

        var grid = box.GetVisualDescendants().OfType<Grid>().First();
        Assert.Equal(4, grid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(4, grid.ColumnDefinitions[^1].Width.Value);
    }
}
