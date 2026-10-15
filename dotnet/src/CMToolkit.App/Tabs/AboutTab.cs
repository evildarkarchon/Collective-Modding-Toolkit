using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Core;

namespace CMToolkit.App.Tabs;

/// <summary>
/// <c>tabs._about.AboutTab</c> (ABOUT-1): the title above the 256 px icon on the left; on the right the version and
/// credits, then a row per link (Nexus Mods, Discord, GitHub) with its logo, a separator, and buttons that open the
/// link (NET-4) or copy it (THR-3).
/// </summary>
public sealed class AboutTab : TabPage
{
    /// <summary><c>NEXUS_LINK</c>.</summary>
    public const string NexusLink = "https://www.nexusmods.com/fallout4/mods/87907";

    /// <summary><c>DISCORD_INVITE</c>.</summary>
    public const string DiscordInvite = "https://discord.gg/tktyEyYHZH";

    /// <summary>
    /// <c>GITHUB_LINK</c>, opened and copied here and by the update banner: the fork that publishes the C# releases
    /// (ADR-0005), whose latest release the update check asks for. The reference linked RowanSkie's fork while checking
    /// wxMichael's releases (bug #32, fixed per ADR-0006).
    /// </summary>
    public const string GitHubLink = "https://github.com/evildarkarchon/Collective-Modding-Toolkit";

    private readonly AppRuntime _runtime;

    /// <summary>The About tab.</summary>
    /// <param name="runtime">The app runtime: its browser, clock and error reporter serve the link buttons.</param>
    public AboutTab(AppRuntime runtime)
        : base("About", "AboutTab")
    {
        _runtime = runtime;
    }

    /// <summary>
    /// <c>"\n".join(APP_TITLE.rsplit(maxsplit=1))</c>: the app name with its last space made a line break.
    /// </summary>
    public static string Title
    {
        get
        {
            // The name is a constant with single spaces, so splitting at the last space is all rsplit does here.
            var last = AppInfo.Name.LastIndexOf(' ');
            return $"{AppInfo.Name[..last]}\n{AppInfo.Name[(last + 1)..]}";
        }
    }

    /// <summary>
    /// The version and credits, centred under one another. The last line credits this port, a deliberate change to
    /// the reference's text (deviation #69); <see cref="BuildContent"/> trims the gap under it so the column still fits.
    /// </summary>
    public static string Text =>
        $"v{AppInfo.Version}\n\nCreated by wxMichael for the\nCollective Modding Community\n#cm-toolkit on Discord\nModified by RowanSkie\nC# port by evildarkarchon";

    /// <inheritdoc/>
    protected internal override Control BuildContent()
    {
        // Column 0 and row 1 have weight 1 from _build_gui, and row 0 kept the weight 1 load() gave it, so the
        // spare height is shared by both rows: TkGrid, not Grid, reproduces the title's resulting position.
        var title = new TextBlock
        {
            Text = Title,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10),
        };
        title.Classes.Add("large");

        // sticky=NS, pady=(0, 20): the label fills the row's height and its image sits at its centre.
        var icon = new AssetImage("images/icon-256.png")
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var iconCell = new Border
        {
            Child = icon,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
            [Grid.RowProperty] = 1,
        };

        // pady=10, except the gap under the text is 7: the credit's line (#69) made the frame 407 px, Tk's page height,
        // and the port's page is 3 px shorter (dotnet/README.md), so without the trim the last link row would overhang.
        var text = new TextBlock
        {
            Text = Text,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 7),
        };

        // frame_about_text: not sticky, so centred in the two rows it spans; padx=(0, 20).
        var about = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0),
            [Grid.ColumnProperty] = 1,
            [Grid.RowSpanProperty] = 2,
            Children =
            {
                text,
                LinkRow("images/logo-nexusmods.png", "Open Link", "Copy Link", NexusLink),
                LinkRow("images/logo-discord.png", "Open Invite", "Copy Invite", DiscordInvite),
                LinkRow("images/logo-github.png", "Open Link", "Copy Link", GitHubLink),
            },
        };

        return new TkGrid
        {
            ColumnWeights = [1, 0],
            RowWeights = [1, 1],
            Children = { title, iconCell, about },
        };
    }

    /// <summary>
    /// One link row (pady=(10, 0), sticky=E): the logo spanning both button rows, a vertical separator, then the open
    /// and copy buttons, each <c>width=12, padding=0</c>.
    /// </summary>
    private Grid LinkRow(string logo, string openText, string copyText, string url)
    {
        var open = LinkButton(openText, 0);
        open.Click += (_, _) => _runtime.Browser.Open(url);
        var copy = LinkButton(copyText, 1);
        CopyTextButton.Attach(copy, url, _runtime);

        // add_separator(VERTICAL): padx=10, pady=10, ipadx=1, sticky=NS, so a 3 px wide bar 20 px shorter than the row.
        var separator = new Border
        {
            Width = 3,
            Margin = new Thickness(10),
            [Grid.ColumnProperty] = 1,
            [Grid.RowSpanProperty] = 2,
        };
        separator.Bind(Border.BackgroundProperty, separator.GetResourceObservable("SvSep"));

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children =
            {
                new AssetImage(logo) { VerticalAlignment = VerticalAlignment.Center, [Grid.RowSpanProperty] = 2 },
                separator,
                open,
                copy,
            },
        };
    }

    /// <summary>
    /// A <c>ttk.Button(width=12, padding=0)</c>: twelve 8 px characters plus the 4 px sprite border on each side, and
    /// one 17 px line plus the same border, so 104x25 whatever its text; pady=5.
    /// </summary>
    private static Button LinkButton(string text, int row) => new()
    {
        Content = text,
        Width = 104,
        Height = 25,
        Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 5),
        [Grid.ColumnProperty] = 2,
        [Grid.RowProperty] = row,
    };
}
