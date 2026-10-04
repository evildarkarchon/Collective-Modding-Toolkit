using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CmtShellPrototype.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Avalonia.Media.TextOptions.SetTextRenderingMode(this, PrototypeState.TextRenderingMode);
        DataContext = PrototypeState.Scenario == Scenario.Problems ? OverviewData.Problems() : OverviewData.Baseline();
        Opened += (_, _) =>
        {
            if (PrototypeState.ForceDwmDarkTitleBar)
            {
                Native.ForceDarkTitleBar(this);
            }
        };
    }

    /// <summary>
    /// Escape quits like the original (<c>root.bind("&lt;Escape&gt;", root.destroy)</c>); ←/→ cycle the prototype
    /// variant so the A/B/C comparison works without the controls window.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Escape:
                (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
                break;
            case Key.Left:
            case Key.Right:
                var count = Enum.GetValues<Variant>().Length;
                var step = e.Key == Key.Right ? 1 : count - 1;
                PrototypeState.Variant = (Variant)(((int)PrototypeState.Variant + step) % count);
                ((App)Application.Current!).Rebuild();
                e.Handled = true;
                break;
        }
    }

    /// <summary>Forces the Game Path tooltip open so its styling can be screenshotted without a real hover.</summary>
    public void ShowGamePathToolTip()
    {
        // Pointer placement needs a real pointer; anchor below the label for the scripted capture instead.
        ToolTip.SetPlacement(GamePathText, PlacementMode.Bottom);
        ToolTip.SetIsOpen(GamePathText, true);
    }

    private void OnDowngradeManager(object? sender, RoutedEventArgs e) => new LogSampleWindow().ShowDialog(this);

    private void OnHedrDetails(object? sender, PointerPressedEventArgs e) => new TreeWindowSample().ShowDialog(this);
}
