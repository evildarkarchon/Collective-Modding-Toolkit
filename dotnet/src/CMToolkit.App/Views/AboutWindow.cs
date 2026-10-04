using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;

namespace CMToolkit.App.Views;

/// <summary>
/// <c>modal_window.AboutWindow</c> (MOD-2): a block of explanatory text above a Close button. Space closes it too.
/// </summary>
public sealed class AboutWindow : ModalWindow
{
    /// <param name="runtime">The app runtime.</param>
    /// <param name="width">The client width; the text also wraps at it.</param>
    /// <param name="height">The client height.</param>
    /// <param name="title">The window title.</param>
    /// <param name="text">The text, shown at 10 pt, left-justified, from the top.</param>
    public AboutWindow(AppRuntime runtime, int width, int height, string title, string text)
        : base(runtime, title, width, height)
    {
        var label = new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            // anchor=N: the wrapped block is centred horizontally at the top of the label.
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.Classes.Add("small");

        // ttk.Button(width=win_width // 2) counts characters, so it asks for far more than the window has, and grid
        // shrinks it to the column: the full width less padx=10 on each side.
        var close = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(10),
        };
        close.Click += (_, _) => RequestClose();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                // sticky=NSEW, padx=10, pady=(10, 0), wraplength=win_width.
                new WraplengthPresenter
                {
                    Wraplength = width,
                    Margin = new Thickness(10, 10, 10, 0),
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = label,
                },
                new Border { [Grid.RowProperty] = 1, Child = close },
            },
        };

        CloseOnSpace();
    }
}
