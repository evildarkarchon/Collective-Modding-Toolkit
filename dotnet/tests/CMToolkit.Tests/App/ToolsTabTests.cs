using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>tabs._tools.ToolsTab</c>. Texts, links and tooltips are proven by the <c>tools-tab</c> scenario; these pin the
/// behaviour around them and the layout, against geometry measured from the reference (Tk 8.6, 100 %).
/// </summary>
public sealed class ToolsTabTests
{
    private static (MainWindow Window, List<GroupBox> Frames, RecordingBrowser Browser) Show(ToolsTab? tab = null, AppRuntime? runtime = null)
    {
        var browser = new RecordingBrowser();
        runtime ??= new AppRuntime(new CapturingLogger()) { Browser = browser };
        var window = TabHost.Show(runtime, tab ?? new ToolsTab(runtime));
        return (window, TabHost.Content(window).GetLogicalChildren().OfType<GroupBox>().ToList(), browser);
    }

    private static List<Button> Buttons(GroupBox frame) => [.. ((Grid)frame.Content!).Children.OfType<Button>()];

    private static Rect BoundsIn(Visual visual, Visual frame)
        => new(visual.TranslatePoint(default, frame)!.Value, visual.Bounds.Size);

    [Theory]
    [Trait("Parity", "TOOL-2")]
    [InlineData("https://www.nexusmods.com/fallout4/mods/1", "View on Nexus Mods")]
    [InlineData("https://github.com/TES5Edit/TES5Edit#xedit", "View on GitHub")]
    [InlineData("https://www.capframex.com/", "Open website")]
    // Nexus Mods is checked first, and the substrings are case-sensitive, as Python's "in" is.
    [InlineData("https://github.com/nexusmods", "View on Nexus Mods")]
    [InlineData("https://GitHub.com/x", "Open website")]
    public void A_link_tooltip_is_picked_by_substring(string url, string expected)
        => Assert.Equal(expected, ToolsTab.LinkTooltip(url));

    [AvaloniaFact]
    [Trait("Parity", "TOOL-2")]
    public void The_info_tooltip_is_on_the_icon_cell_and_shows_at_once()
    {
        var (_, frames, _) = Show();
        var cell = ((Grid)frames[1].Content!).Children.OfType<Border>().First();

        Assert.StartsWith("Bethini Pie (Performance INI Editor)", ToolTip.GetTip(cell) as string);
        Assert.Equal(0, ToolTip.GetShowDelay(cell));
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void The_utility_buttons_are_live_and_open_no_web_page_without_a_window_opener()
    {
        var (_, frames, browser) = Show();

        foreach (var button in Buttons(frames[0]))
        {
            Assert.True(button.IsEnabled);
            MouseClick.On(button);
        }

        Assert.Empty(browser.Opened);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void The_utility_buttons_open_the_windows_they_are_given()
    {
        var opened = new List<string>();
        var runtime = new AppRuntime(new CapturingLogger());
        var (_, frames, _) = Show(new ToolsTab(runtime, () => opened.Add("Downgrader"), () => opened.Add("ArchivePatcher")), runtime);

        Buttons(frames[0]).ForEach(MouseClick.On);

        Assert.Equal(["Downgrader", "ArchivePatcher"], opened);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void The_columns_have_centred_titles_and_share_the_spare_width_equally()
    {
        var (window, frames, _) = Show();

        Assert.All(frames, f => Assert.Contains("centred", f.Classes));
        // The CentredGroupBox theme's header border is centred over the frame.
        Assert.All(frames, f => Assert.Contains(
            f.GetVisualDescendants().OfType<Border>(), b => b.HorizontalAlignment == Avalonia.Layout.HorizontalAlignment.Center));
        Assert.All(frames, f => Assert.Equal(13, ((TextBlock)f.Header!).FontSize));

        // Tk: 180/265/257 px of content, 5 px pad each, then 11 spare px shared 3/4/4 across a 758 px page. Our page is
        // 754 px, so the shares are 2/2/3: each column is its content + 5 + a near-equal share, not a third of the page.
        Assert.Equal([187, 272, 265], frames.Select(f => f.Bounds.Width));
        Assert.All(frames, f => Assert.Equal(TabHost.Content(window).Bounds.Height - 10, f.Bounds.Height));
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void Buttons_are_as_wide_as_the_widest_in_their_column_and_sized_like_ttk_buttons()
    {
        var (_, frames, _) = Show();

        // Measured in Tk: the widest buttons are 160, 224 and 216 px; one line is 30 px tall, two are 47.
        Assert.Equal([160, 224, 216], frames.Select(f => Buttons(f).Select(b => b.Bounds.Width).Distinct().Single()));
        Assert.All(frames.SelectMany(Buttons), b =>
            Assert.Equal(((string)b.Content!).Contains('\n') ? 47 : 30, b.Bounds.Height));

        // padding=5 inside the frame, padx=(10, 0), pady=5, under the 17 px title: the first button 15 px in, 27 down.
        var first = Buttons(frames[0])[0];
        Assert.Equal(new Point(15, 27), BoundsIn(first, frames[0]).Position);
        Assert.Equal(40, BoundsIn(Buttons(frames[0])[1], frames[0]).Y - BoundsIn(first, frames[0]).Y);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void Each_link_button_has_its_info_icon_5px_to_its_right_centred_below_a_7px_offset()
    {
        var (_, frames, _) = Show();
        var grid = (Grid)frames[1].Content!;

        foreach (var button in Buttons(frames[1]))
        {
            var icon = grid.Children.OfType<Border>().Single(b => Grid.GetRow(b) == Grid.GetRow(button)).Child!;
            var buttonBox = BoundsIn(button, frames[1]);
            var iconBox = BoundsIn(icon, frames[1]);

            Assert.Equal("images/info-16.png", ((AssetImage)icon).AssetPath);
            Assert.Equal(new Size(16, 16), iconBox.Size);
            Assert.Equal(buttonBox.Right + 5, iconBox.X);
            // Tk: the 16 px image centred in the row's height (button + 10) less the 7 px top pad: 10 px below a
            // one-line button's top (Tk floors the half pixel; layout rounding may take the other side).
            Assert.InRange(iconBox.Y - buttonBox.Y, 2 + ((buttonBox.Height + 10 - 7 - 16) / 2) - 0.5, 2 + ((buttonBox.Height + 10 - 7 - 16) / 2) + 0.5);
        }
    }

    [AvaloniaFact]
    [Trait("Parity", "NET-4")]
    public void A_link_button_opens_its_url_in_the_browser()
    {
        var (_, frames, browser) = Show();

        MouseClick.On(Buttons(frames[2])[0]);

        Assert.Equal(["https://github.com/TES5Edit/TES5Edit#xedit"], browser.Opened);
    }

    [AvaloniaFact]
    [Trait("Parity", "TOOL-1")]
    public void A_button_label_is_a_left_justified_block_centred_in_the_button()
    {
        // "Creation Kit Platform\n   Extended (CKPE)": Tk draws the second line three characters in, from the
        // spaces, under a block centred in the 216 px button.
        var (_, frames, _) = Show();
        var button = Buttons(frames[2])[1];
        var text = button.GetVisualDescendants().OfType<TextBlock>().Single();

        Assert.Equal(Avalonia.Media.TextAlignment.Start, text.TextAlignment);
        Assert.Equal((button.Bounds.Width - text.Bounds.Width) / 2, text.TranslatePoint(default, button)!.Value.X, 0.5);
    }
}
