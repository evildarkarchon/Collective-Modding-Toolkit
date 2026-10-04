using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CMToolkit.App.Views;

/// <summary>Which <c>tkinter.messagebox</c> function a message box stands in for.</summary>
public enum MessageBoxKind
{
    /// <summary><c>showwarning</c>: a warning icon and OK.</summary>
    Warning,

    /// <summary><c>showerror</c>: an error icon and OK.</summary>
    Error,

    /// <summary><c>askyesno</c>: a question icon, Yes and No.</summary>
    Question,
}

/// <summary>
/// The app's message box (decided on issue #9): an sv-styled dialog in place of the light native boxes
/// <c>tkinter.messagebox</c> showed. Its body is the message beside an icon; its footer strip holds the buttons. Use
/// <see cref="MessageBox"/> to show one.
/// </summary>
/// <remarks>
/// Like the native boxes: Enter presses the first button. Escape dismisses an OK box; a Yes/No box has no Cancel, so it
/// ignores Escape. Closing a box from its title bar answers No.
/// </remarks>
public sealed class MessageBoxWindow : Window
{
    private static readonly Uri WarningIcon = new("avares://cm-toolkit/Assets/Images/warning-16.png");

    private readonly TaskCompletionSource<bool> _answer = new();

    /// <param name="kind">The icon and buttons.</param>
    /// <param name="title">The window title.</param>
    /// <param name="message">The message; it wraps at 420 px.</param>
    public MessageBoxWindow(MessageBoxKind kind, string title, string message)
    {
        Kind = kind;
        Title = title;
        Icon = AppIcon.Load();
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        CanMaximize = false;
        CanMinimize = false;
        ShowInTaskbar = false;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (kind == MessageBoxKind.Question)
        {
            buttons.Children.Add(AnswerButton("Yes", answer: true, isDefault: true, isCancel: false));
            buttons.Children.Add(AnswerButton("No", answer: false, isDefault: false, isCancel: false));
        }
        else
        {
            buttons.Children.Add(AnswerButton("OK", answer: true, isDefault: true, isCancel: true));
        }

        var footer = new Border { Padding = new Thickness(12), Child = buttons, [DockPanel.DockProperty] = Dock.Bottom };
        footer.Bind(Border.BackgroundProperty, footer.GetResourceObservable("ButtonBackground"));

        var text = new TextBlock { Text = message, MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
        text.Classes.Add("small");

        Content = new DockPanel
        {
            Children =
            {
                footer,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(20, 20, 30, 24),
                    Spacing = 14,
                    Children = { BuildIcon(kind), text },
                },
            },
        };

        Closed += (_, _) => _answer.TrySetResult(false);
    }

    /// <summary>Which messagebox function this box stands in for.</summary>
    public MessageBoxKind Kind { get; }

    /// <summary>
    /// Shows the box, modal over <paramref name="owner"/> and centred on it, or, with no owner (startup, before the main
    /// window exists), on its own and centred on the screen.
    /// </summary>
    /// <returns>Whether OK or Yes was pressed; <see langword="false"/> for No or a close.</returns>
    public Task<bool> ShowAsync(Window? owner)
    {
        if (owner is null)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Show();
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _ = ShowDialog(owner);
        }

        return _answer.Task;
    }

    private Button AnswerButton(string label, bool answer, bool isDefault, bool isCancel)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        button.Click += (_, _) =>
        {
            _answer.TrySetResult(answer);
            Close();
        };
        return button;
    }

    /// <summary>
    /// The 32 px icon. The warning is the app's own warning image (approved in the main-window-shell prototype); the
    /// app has no error or question image, so those are drawn in the palette's bad and info colours.
    /// </summary>
    private static Control BuildIcon(MessageBoxKind kind)
    {
        if (kind == MessageBoxKind.Warning)
        {
            using var stream = AssetLoader.Open(WarningIcon);
            return new Image { Source = new Bitmap(stream), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top };
        }

        var disc = new Ellipse { Width = 32, Height = 32 };
        disc.Bind(Shape.FillProperty, disc.GetResourceObservable(kind == MessageBoxKind.Error ? "CmtBad" : "CmtInfo"));
        Control glyph = kind == MessageBoxKind.Error
            ? new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M 11,11 L 21,21 M 21,11 L 11,21"),
                Stroke = Brushes.White,
                StrokeThickness = 2.5,
            }
            : new TextBlock
            {
                Text = "?",
                Foreground = Brushes.White,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        return new Panel { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Top, Children = { disc, glyph } };
    }
}

/// <summary>
/// Shows message boxes: <c>tkinter.messagebox.showwarning</c>, <c>showerror</c> and <c>askyesno</c>, made async.
/// Callers that relied on the Tk call blocking must await these instead.
/// </summary>
public static class MessageBox
{
    /// <summary>Shows a warning and completes when it is dismissed.</summary>
    /// <param name="owner">The window it is modal over, or <see langword="null"/> before the main window exists.</param>
    public static Task ShowWarningAsync(Window? owner, string title, string message)
        => new MessageBoxWindow(MessageBoxKind.Warning, title, message).ShowAsync(owner);

    /// <summary>Shows an error and completes when it is dismissed.</summary>
    /// <param name="owner">The window it is modal over, or <see langword="null"/> before the main window exists.</param>
    public static Task ShowErrorAsync(Window? owner, string title, string message)
        => new MessageBoxWindow(MessageBoxKind.Error, title, message).ShowAsync(owner);

    /// <summary>Asks a yes/no question.</summary>
    /// <param name="owner">The window it is modal over, or <see langword="null"/> before the main window exists.</param>
    /// <returns><see langword="true"/> for Yes; <see langword="false"/> for No or a close.</returns>
    public static Task<bool> AskYesNoAsync(Window? owner, string title, string message)
        => new MessageBoxWindow(MessageBoxKind.Question, title, message).ShowAsync(owner);
}
