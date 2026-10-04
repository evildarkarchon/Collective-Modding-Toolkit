using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace CmtPanesPrototype.Views;

/// <summary>
/// PROTOTYPE switcher + state readout. A separate window parked left of the main one so the main window and its
/// panes stay clean for screenshots. Deliberately styled unlike the app (Segoe UI, magenta frame).
/// </summary>
public sealed class PrototypeBar : Window
{
    private readonly TextBlock _variantLabel = new() { VerticalAlignment = VerticalAlignment.Center, Width = 300 };
    private readonly TextBlock _readout = new() { FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = Brushes.LightGray };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private MainWindow? _main;

    public PrototypeBar()
    {
        Title = "PROTOTYPE controls (scanner panes)";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
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
            Width = 640,
            Children =
            {
                Row("Variant",
                    Btn("A", () => SetVariant(Variant.A)), Btn("B", () => SetVariant(Variant.B)), Btn("C", () => SetVariant(Variant.C)),
                    _variantLabel),
                Row("Select",
                    Btn("Wrong Version", () => _main?.Pick("Wrong Version")),
                    Btn("Race Subgraph (File List)", () => _main?.Pick("Race Subgraph")),
                    Btn("Invalid Archive (long)", () => _main?.Pick("Invalid Archive")),
                    Btn("Junk (Auto-Fix)", () => _main?.Pick("Junk"))),
                Row("Lifecycle",
                    Btn("Scan Game", () => _main?.StartScan()),
                    Btn("Tab: Overview", () => _main?.SelectTab(0)),
                    Btn("Tab: Scanner", () => _main?.SelectTab(2))),
                Row("Move main",
                    Btn("+137/+61", () => Nudge(137, 61)),
                    Btn("Other monitor", MoveToOtherMonitor),
                    Btn("Straddle monitors", Straddle),
                    Btn("Minimise 2 s", MinimiseBriefly)),
                new TextBlock
                {
                    Text = "Readout (physical px; expected = Tk update_geometry formulas from the client origin):",
                    Foreground = Brushes.Magenta,
                },
                _readout,
            },
        };

        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    /// <summary>Re-targets the bar at a (possibly rebuilt) main window and parks it to the left of it.</summary>
    public void AttachTo(MainWindow main)
    {
        _main = main;
        void Park()
        {
            var outer = Native.WindowRect(main);
            var width = (int)((Bounds.Width > 0 ? Bounds.Width : 660) * DesktopScaling);
            Position = new PixelPoint(Math.Max(0, outer.X - width - 12), outer.Y);
        }

        if (main.IsVisible)
        {
            Park();
        }
        else
        {
            main.Opened += (_, _) => Park();
        }
    }

    private static StackPanel Row(string label, params Control[] items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Magenta });
        row.Children.AddRange(items);
        return row;
    }

    private static Button Btn(string text, Action onClick)
    {
        var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(8, 3) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void SetVariant(Variant v)
    {
        PrototypeState.Variant = v;
        ((App)Application.Current!).Rebuild();
    }

    private void Nudge(int dx, int dy)
    {
        if (_main is { } m)
        {
            m.Position = new PixelPoint(m.Position.X + dx, m.Position.Y + dy);
        }
    }

    /// <summary>Moves the main window to the same relative spot on the next monitor (wrapping).</summary>
    private void MoveToOtherMonitor()
    {
        if (_main is not { } m || m.Screens.All.Count < 2)
        {
            return;
        }

        var screens = m.Screens.All.ToList();
        var current = m.Screens.ScreenFromWindow(m) ?? screens[0];
        var next = screens[(screens.IndexOf(current) + 1) % screens.Count];
        var offset = m.Position - current.Bounds.Position;
        m.Position = next.Bounds.Position + offset;
    }

    /// <summary>Puts the main window's right edge just short of its monitor's right edge, so the side pane spills onto the neighbour.</summary>
    private void Straddle()
    {
        if (_main is not { } m || m.Screens.ScreenFromWindow(m) is not { } screen)
        {
            return;
        }

        var outer = Native.WindowRect(m);
        m.Position = new PixelPoint(screen.Bounds.Right - outer.Width + 8 - 60, m.Position.Y);
    }

    private void MinimiseBriefly()
    {
        if (_main is not { } m)
        {
            return;
        }

        m.WindowState = WindowState.Minimized;
        DispatcherTimer.RunOnce(() => m.WindowState = WindowState.Normal, TimeSpan.FromSeconds(2));
    }

    private void Refresh()
    {
        _variantLabel.Text = PrototypeState.Describe(PrototypeState.Variant);
        if (_main is { } m)
        {
            _readout.Text = Readout.Describe(m);
        }
    }
}
