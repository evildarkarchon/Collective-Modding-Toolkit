using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CMToolkit.App.Runtime;
using CMToolkit.App.Tabs;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace CMToolkit.Tests.App;

/// <summary>
/// <c>tabs._about.AboutTab</c> and <c>utils.copy_text_button</c>. Texts, links and the copy round trip are proven by
/// the <c>about-tab</c> scenario; these pin the layout measured from the reference and the copy button's timing.
/// </summary>
public sealed class AboutTabTests
{
    private static (MainWindow Window, Panel Page, FakeTimeProvider Time, RecordingBrowser Browser) Show()
    {
        var time = new FakeTimeProvider();
        var browser = new RecordingBrowser();
        var runtime = new AppRuntime(new CapturingLogger()) { Time = time, Browser = browser };
        var window = TabHost.Show(runtime, new AboutTab(runtime));
        return (window, (Panel)TabHost.Content(window), time, browser);
    }

    private static Point At(Visual visual, Visual page) => visual.TranslatePoint(default, page)!.Value;

    private static List<Grid> LinkRows(Panel page) => [.. page.Children.OfType<StackPanel>().Single().Children.OfType<Grid>()];

    [AvaloniaFact]
    [Trait("Parity", "ABOUT-1")]
    public void The_title_is_the_app_name_split_at_its_last_space_at_20pt()
    {
        var (_, page, _, _) = Show();
        var title = page.Children.OfType<TextBlock>().Single();

        Assert.Equal("Collective Modding\nToolkit", AboutTab.Title);
        Assert.Equal(27, title.FontSize);
        Assert.Equal(Avalonia.Media.TextAlignment.Center, title.TextAlignment);
        // 18 Cascadia characters at 16 px, two 35 px lines (Tk: 288x70). Avalonia's 27 px line measures 1 px wider
        // than its advances add up to (dotnet/README.md, Known residuals); centred, that moves it half a pixel.
        Assert.Equal(70, title.Bounds.Height);
        Assert.InRange(title.Bounds.Width, 288, 289);
    }

    [AvaloniaFact]
    [Trait("Parity", "ABOUT-1")]
    public void The_rows_share_the_spare_height_as_tk_did()
    {
        var (_, page, _, _) = Show();
        var title = page.Children.OfType<TextBlock>().Single();
        var icon = page.Children.OfType<Border>().Single();

        // Rows of 90 and 276 px, both weight 1, under a 386 px text frame spanning them: 100/286 after the span, then
        // the spare height shared. On Tk's 407 px page that put the title at y 20 and the icon row at 110.
        var spare = page.Bounds.Height - 386;
        var row0 = 100 + Math.Truncate(spare / 2);
        Assert.Equal((row0 - 70) / 2, At(title, page).Y, 0.5);
        Assert.Equal(row0, At(icon, page).Y);
        Assert.Equal(page.Bounds.Height - row0 - 20, icon.Bounds.Height);
        Assert.Equal(new Size(256, 256), ((AssetImage)icon.Child!).Bounds.Size);
    }

    [AvaloniaFact]
    [Trait("Parity", "ABOUT-1")]
    public void The_text_column_sits_20px_from_the_right_with_right_aligned_link_rows()
    {
        var (_, page, _, _) = Show();
        var about = page.Children.OfType<StackPanel>().Single();
        var rows = LinkRows(page);

        // Tk: a 383x386 frame, centred vertically, 20 px in from the right edge.
        Assert.Equal(new Size(383, 386), about.Bounds.Size);
        Assert.Equal(page.Bounds.Width - 20, At(about, page).X + about.Bounds.Width);
        Assert.Equal(["images/logo-nexusmods.png", "images/logo-discord.png", "images/logo-github.png"],
            rows.Select(r => r.Children.OfType<AssetImage>().Single().AssetPath));
        Assert.All(rows, r => Assert.Equal(70, r.Bounds.Height));
        Assert.All(rows, r => Assert.Equal(about.Bounds.Width, At(r, about).X + r.Bounds.Width));
        // The 16 px text: six 21 px lines, 10 px from the frame's top.
        var text = about.Children.OfType<TextBlock>().Single();
        Assert.Equal(new Size(252, 126), text.Bounds.Size);
        Assert.Equal(10, At(text, about).Y);
    }

    [AvaloniaFact]
    [Trait("Parity", "ABOUT-1")]
    public void Link_buttons_are_104_by_25_beside_a_3px_separator_20px_shorter_than_the_row()
    {
        var (_, page, _, _) = Show();

        foreach (var row in LinkRows(page))
        {
            var separator = row.Children.OfType<Border>().Single();
            Assert.Equal(new Size(3, 50), separator.Bounds.Size);
            Assert.All(row.Children.OfType<Button>(), b => Assert.Equal(new Size(104, 25), b.Bounds.Size));
            Assert.Equal(At(separator, row).X + 3 + 10, At(row.Children.OfType<Button>().First(), row).X);
        }
    }

    [AvaloniaFact]
    [Trait("Parity", "NET-4")]
    public void Open_opens_the_link_in_the_browser()
    {
        var (_, page, _, browser) = Show();

        MouseClick.On(LinkRows(page)[1].Children.OfType<Button>().First());

        Assert.Equal([AboutTab.DiscordInvite], browser.Opened);
    }

    [AvaloniaFact]
    [Trait("Parity", "THR-3")]
    public void Copy_shows_copied_disabled_for_exactly_3s_then_its_own_text_again()
    {
        var (window, page, time, _) = Show();
        var copy = LinkRows(page)[2].Children.OfType<Button>().Last();

        MouseClick.On(copy);
        DispatcherWait.Until(() => !copy.IsEnabled);
        Assert.Equal("Copied!", copy.Content);
        Assert.Equal(AboutTab.GitHubLink, window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult());

        // Disabled, so a second click does nothing and the clock isn't restarted.
        MouseClick.On(copy);
        time.Advance(TimeSpan.FromMilliseconds(2999));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Copied!", copy.Content);

        time.Advance(TimeSpan.FromMilliseconds(1));
        DispatcherWait.Until(() => copy.IsEnabled);
        Assert.Equal("Copy Link", copy.Content);
    }

    [AvaloniaFact]
    [Trait("Parity", "THR-3")]
    public void Each_copy_button_reverts_on_its_own_clock()
    {
        var (_, page, time, _) = Show();
        var first = LinkRows(page)[0].Children.OfType<Button>().Last();
        var second = LinkRows(page)[1].Children.OfType<Button>().Last();

        MouseClick.On(first);
        time.Advance(TimeSpan.FromSeconds(2));
        MouseClick.On(second);
        time.Advance(TimeSpan.FromSeconds(1));
        DispatcherWait.Until(() => first.IsEnabled);

        Assert.Equal("Copy Link", first.Content);
        Assert.Equal("Copied!", second.Content);
    }
}
