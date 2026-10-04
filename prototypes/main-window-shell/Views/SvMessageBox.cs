using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CmtShellPrototype.Views;

/// <summary>
/// PROTOTYPE message-box option 1: our own sv_ttk-styled dialog. Async (ShowDialog), so callers can no longer rely
/// on the call blocking the way tkinter.messagebox does.
/// </summary>
public sealed class SvMessageBox : Window
{
    public SvMessageBox(string title, string message)
    {
        Title = title;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = "OK", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
        ok.Click += (_, _) => Close();

        using var icon = AssetLoader.Open(new Uri("avares://CmtShellPrototype/Assets/Images/warning-16.png"));
        Content = new DockPanel
        {
            Margin = new Thickness(0),
            Children =
            {
                new Border
                {
                    [DockPanel.DockProperty] = Dock.Bottom,
                    Background = new SolidColorBrush(Color.Parse("#2A2A2A")),
                    Padding = new Thickness(12),
                    Child = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Children = { ok } },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(20, 20, 30, 24),
                    Spacing = 14,
                    Children =
                    {
                        new Image { Source = new Bitmap(icon), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top },
                        new TextBlock { Text = message, FontSize = 13, MaxWidth = 420, TextWrapping = TextWrapping.Wrap },
                    },
                },
            },
        };

        KeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape)
            {
                Close();
            }
        };
    }
}
