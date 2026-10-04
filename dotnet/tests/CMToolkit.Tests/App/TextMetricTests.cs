using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace CMToolkit.Tests.App;

/// <summary>
/// The text-metric pins from the main-window-shell prototype (issue #9): Tk lays Cascadia Mono out with GDI's
/// line pitch and whole-pixel advances, so every TextBlock gets a LineHeight and LetterSpacing per font size.
/// </summary>
[Trait("Parity", "SHELL-14")]
public sealed class TextMetricTests
{
    private static T Shown<T>(T content)
        where T : Control
    {
        new Window { Content = content }.Show();
        return content;
    }

    [AvaloniaFact]
    public void Plain_text_uses_the_16px_pins()
    {
        var text = Shown(new TextBlock { Text = "Mod Manager:" });

        Assert.Equal("Cascadia Mono", text.FontFamily.Name);
        Assert.Equal(16, text.FontSize);
        Assert.Equal(21, text.LineHeight);
        Assert.Equal(-0.375, text.LetterSpacing);
    }

    [AvaloniaFact]
    public void Small_text_uses_the_13px_pins()
    {
        var text = Shown(new TextBlock { Text = "Binaries (EXE/DLL/BIN)", Classes = { "small" } });

        Assert.Equal(13, text.FontSize);
        Assert.Equal(17, text.LineHeight);
        Assert.Equal(0.383, text.LetterSpacing);
    }

    [AvaloniaFact]
    public void Button_text_is_small_even_though_it_renders_through_a_textblock_subclass()
    {
        // Button string content renders through AccessText, which a plain "TextBlock" type selector would miss.
        var button = Shown(new Button { Content = "Downgrade Manager..." });
        var text = button.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.Equal(13, text.FontSize);
        Assert.Equal(17, text.LineHeight);
        Assert.Equal(0.383, text.LetterSpacing);
    }

    [AvaloniaFact]
    public void A_16px_line_is_21px_tall_like_gdi_and_not_the_tighter_typo_metric_height()
    {
        var text = Shown(new TextBlock { Text = "Version:" });
        text.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(21, text.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void Sixteen_px_text_advances_9px_per_character_like_gdi_rounding()
    {
        // GDI rounds Cascadia's 9.375 px advance to 9; the -0.375 letter spacing restores Tk's line widths.
        var text = Shown(new TextBlock { Text = "0123456789" });
        text.Measure(new Avalonia.Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.Equal(90, text.DesiredSize.Width, precision: 0);
    }
}
