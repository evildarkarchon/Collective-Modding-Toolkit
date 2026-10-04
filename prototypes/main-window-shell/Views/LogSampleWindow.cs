using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CmtShellPrototype.Views;

/// <summary>
/// PROTOTYPE stand-in for the Downgrader's log pane (logger.Logger): read-only text, ❌ ✅ 💭 prefixes coloured by
/// log type, the message itself in the default colour, an always-visible scrollbar. Compare with python-downgrader.png.
/// Built in code because the emoji font switch is per-Run.
/// </summary>
public sealed class LogSampleWindow : Window
{
    private static readonly (string Emoji, IBrush Brush, string Text)[] Lines =
    [
        ("💭 ", new SolidColorBrush(Color.Parse("#1E90FF")), "Patches will be downloaded and applied as-needed."),
        ("💭 ", new SolidColorBrush(Color.Parse("#1E90FF")), "Downloading patch: Fallout4.exe (Next-Gen -> Old-Gen)"),
        ("✅ ", new SolidColorBrush(Color.Parse("#619267")), "Patched Fallout4.exe"),
        ("❌ ", new SolidColorBrush(Color.Parse("#AF5A66")), "Failed to patch steam_api64.dll: file is in use by another process. Close the game and any launchers, then try again."),
        ("✅ ", new SolidColorBrush(Color.Parse("#619267")), "Patched Fallout4Launcher.exe"),
    ];

    public LogSampleWindow()
    {
        Title = "Downgrader";
        Width = 600;
        Height = 334;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var text = new SelectableTextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#FAFAFA")),
            Margin = new Thickness(2),
            Inlines = [],
        };
        for (var i = 0; i < 4; i++)
        {
            foreach (var (emoji, brush, line) in Lines)
            {
                var run = new Run(emoji) { Foreground = brush };
                if (PrototypeState.EmojiMode == EmojiMode.Mono)
                {
                    // Tk 8.6 cannot draw colour glyphs: GDI falls back to Segoe UI Symbol and tints it with the tag colour.
                    run.FontFamily = new FontFamily("Segoe UI Symbol");
                }

                text.Inlines!.Add(run);
                text.Inlines.Add(new Run(line + "\n"));
            }
        }

        var scroller = new ScrollViewer
        {
            Content = text,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        Content = new DockPanel
        {
            Children =
            {
                new TextBlock
                {
                    [DockPanel.DockProperty] = Dock.Top,
                    Text = $"Log sample - emoji mode: {PrototypeState.EmojiMode}",
                    FontSize = 13,
                    Margin = new Thickness(10),
                    HorizontalAlignment = HorizontalAlignment.Left,
                },
                new Border
                {
                    BorderBrush = new SolidColorBrush(Color.Parse("#8A8A8A")),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(8, 0, 8, 8),
                    Child = scroller,
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
