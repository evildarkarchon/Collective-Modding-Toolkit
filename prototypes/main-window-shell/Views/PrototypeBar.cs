using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace CmtShellPrototype.Views;

/// <summary>
/// PROTOTYPE switcher. A separate window parked under the main one, so the 760x450 main window stays clean for
/// side-by-side screenshots. Deliberately styled unlike the app (Segoe UI, magenta frame).
/// </summary>
public sealed class PrototypeBar : Window
{
    private const string CccWarning =
        "Fallout4.ccc not found.\nCC files may not be detected. Verifying Steam files or reinstalling should fix this.";

    private readonly TextBlock _variantLabel = new() { VerticalAlignment = VerticalAlignment.Center, Width = 330, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _stateLabel = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
    private MainWindow? _main;

    public PrototypeBar()
    {
        Title = "PROTOTYPE controls";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = true;
        FontFamily = new FontFamily("Segoe UI");
        Background = new SolidColorBrush(Color.Parse("#101018"));
        BorderBrush = Brushes.Magenta;
        BorderThickness = new Thickness(2);

        // Opt out of the app's Cascadia/Tk text tuning; this window is prototype chrome, not part of the design.
        Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.Is<TextBlock>(x))
        {
            Setters =
            {
                new Avalonia.Styling.Setter(TextBlock.FontSizeProperty, 12.0),
                new Avalonia.Styling.Setter(TextBlock.LineHeightProperty, double.NaN),
                new Avalonia.Styling.Setter(TextBlock.LetterSpacingProperty, 0.0),
            },
        });

        Content = new StackPanel
        {
            Margin = new Thickness(8),
            Spacing = 6,
            Children =
            {
                Row("Variant",
                    Btn("◀", () => Cycle(-1)), _variantLabel, Btn("▶", () => Cycle(+1))),
                Row("Data",
                    Btn("Baseline (python-overview.png)", () => SetScenario(Scenario.Baseline)),
                    Btn("MO2 + problems", () => SetScenario(Scenario.Problems))),
                Row("Message box",
                    Btn("Custom sv dialog", () => Open("msg-custom")),
                    Btn("Win32 MessageBoxW", () => Open("msg-win32")),
                    Btn("MessageBox.Avalonia", () => Open("msg-msbox"))),
                Row("Log / emoji",
                    Btn("Colour", () => Open("log-colour")),
                    Btn("Mono (Segoe UI Symbol)", () => Open("log-mono"))),
                Row("Other",
                    Btn("TreeWindow (TableView)", () => Open("tree")),
                    Btn("Toggle DWM dark-title call", () => { PrototypeState.ForceDwmDarkTitleBar ^= true; Rebuild(); })),
                _stateLabel,
            },
        };
        Refresh();
    }

    /// <summary>
    /// Opens one of the comparison dialogs over the main window. Shared by the buttons and <c>--open</c>, so every
    /// dialog can be screenshotted from a script.
    /// </summary>
    public void Open(string what)
    {
        var owner = _main!;
        switch (what)
        {
            case "msg-custom":
                _ = new SvMessageBox("Warning", CccWarning).ShowDialog(owner);
                break;
            case "msg-win32":
                Native.WarningBox(owner, "Warning", CccWarning);
                break;
            case "msg-msbox":
                _ = MessageBoxManager
                    .GetMessageBoxStandard("Warning", CccWarning, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning)
                    .ShowWindowDialogAsync(owner);
                break;
            case "log-colour":
            case "log-mono":
                PrototypeState.EmojiMode = what == "log-mono" ? EmojiMode.Mono : EmojiMode.Colour;
                _ = new LogSampleWindow().ShowDialog(owner);
                break;
            case "tree":
                _ = new TreeWindowSample().ShowDialog(owner);
                break;
            case "tooltip":
                owner.ShowGamePathToolTip();
                break;
        }

        Refresh();
    }

    /// <summary>Re-targets the bar at a (possibly rebuilt) main window and parks it just below it.</summary>
    public void AttachTo(MainWindow main)
    {
        _main = main;
        void Park()
        {
            var scale = main.DesktopScaling;
            var frame = main.FrameSize ?? new Size(main.Width, main.Height);
            Position = new PixelPoint(main.Position.X, main.Position.Y + (int)(frame.Height * scale) + 4);
        }

        if (main.IsVisible)
        {
            Park();
        }
        else
        {
            main.Opened += (_, _) => Park();
        }

        Refresh();
    }

    private static StackPanel Row(string label, params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Magenta });
        row.Children.AddRange(items);
        return row;
    }

    private static Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(8, 3) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Cycle(int step)
    {
        var count = Enum.GetValues<Variant>().Length;
        PrototypeState.Variant = (Variant)(((int)PrototypeState.Variant + step + count) % count);
        Rebuild();
    }

    private void SetScenario(Scenario s)
    {
        PrototypeState.Scenario = s;
        Rebuild();
    }

    private void Rebuild()
    {
        ((App)Application.Current!).Rebuild();
        Refresh();
    }

    private void Refresh()
    {
        _variantLabel.Text = Variants.Describe(PrototypeState.Variant);
        _stateLabel.Text = $"variant={PrototypeState.Variant}  data={PrototypeState.Scenario}  emoji={PrototypeState.EmojiMode}  dwmDarkCall={PrototypeState.ForceDwmDarkTitleBar}   (←/→ in the main window also cycles)";
    }
}
