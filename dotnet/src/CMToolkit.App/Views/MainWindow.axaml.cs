using Avalonia.Controls;

namespace CMToolkit.App.Views;

public partial class MainWindow : Window
{
    /// <summary>
    /// Builds the shell and places it where the Reference Implementation does (SHELL-6). The position is set before
    /// the window is first shown, with <see cref="WindowStartupLocation.Manual"/> in the XAML, so it opens there
    /// rather than jumping after a first frame elsewhere.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        if (Screens.Primary is { } primary)
        {
            Position = ShellPlacement.TkCentredOrigin(primary.Bounds, primary.Scaling);
        }
    }
}
