using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CMToolkit.App.Views;

/// <summary>
/// A copy of PyInstaller's windowed-bootloader dialog, which is what the Reference Implementation showed when an
/// exception escaped while the app was being built (SHELL-13, as decided on issue #18). It keeps PyInstaller's wording,
/// in the app's sv styling: the title, a line naming the exception's message, a read-only box with the stack trace, and
/// a Close button. The app exits with code 1 when it closes.
/// </summary>
public sealed class StartupFailureDialog : Window
{
    /// <param name="exception">What stopped the app being built.</param>
    public StartupFailureDialog(Exception exception)
    {
        Title = "Unhandled exception in script";
        Icon = AppIcon.Load();
        Width = 640;
        Height = 360;
        CanResize = false;
        CanMaximize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var message = new TextBlock
        {
            // PyInstaller formats str(exc); a .NET exception's Message is the same thing.
            Text = $"Failed to execute script 'main' due to unhandled exception: {exception.Message}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(20, 20, 20, 12),
            [DockPanel.DockProperty] = Dock.Top,
        };
        message.Classes.Add("small");

        var trace = new TextBox
        {
            Text = exception.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(20, 0, 20, 20),
        };
        trace.Bind(TextBox.FontSizeProperty, trace.GetResourceObservable("CmtFontSmall"));

        var close = new Button
        {
            Content = "Close",
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true,
        };
        close.Click += (_, _) => Close();

        var footer = new Border { Padding = new Thickness(12), Child = close, [DockPanel.DockProperty] = Dock.Bottom };
        footer.Bind(Border.BackgroundProperty, footer.GetResourceObservable("ButtonBackground"));

        Content = new DockPanel { Children = { message, footer, trace } };
    }
}
