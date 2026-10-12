using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CMToolkit.App.Runtime;
using CMToolkit.App.ViewModels;
using CMToolkit.Core;
using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.F4se;

namespace CMToolkit.App.Tabs;

/// <summary>
/// One row of the F4SE tree: the DLL's name, the glyph of each cell and the row's tag (<c>tree_dlls.insert(...)</c>).
/// </summary>
/// <param name="Name">The file name (the <c>#0</c> column).</param>
/// <param name="Cells">The OG, NG, AE and Your Game cells' glyphs; a non-plugin row has only the first three.</param>
/// <param name="Tag">The row's tag, which colours the whole row.</param>
public sealed record F4seRow(string Name, IReadOnlyList<string> Cells, F4seRowTag Tag);

/// <summary>
/// <c>tabs._f4se.F4SETab</c>: every DLL in <c>Data\F4SE\Plugins</c> with its OG/NG/AE support and its verdict for the
/// game (F4SE-1..F4SE-7), beside a legend.
/// </summary>
/// <remarks>
/// The inspection runs as a blocking operation, as the reference ran it on the UI thread (ADR-0003). A DLL the loader
/// refuses is logged and listed as a non-plugin row, and the rest load (B-18); in the reference it failed the whole
/// load and left the tab on its loading text for good (bug #34, fixed per ADR-0006). The verdicts are taken when the
/// tree is built, from the Install Type the Overview found by then, and never redone.
/// </remarks>
public sealed class F4seTab : TabPage
{
    /// <summary><c>ABOUT_F4SE_DLLS</c> (<c>globals.py</c>), the legend on the right.</summary>
    public const string AboutText =
        "This checks all DLLs in\nData/F4SE/Plugins/ for\nversion-specific code to\ndetermine OG/NG support.\n\n" +
        "\u2714 Version is supported\n\n\u2731 DLL uses Address Library\n\n\u2753 Not an F4SE DLL.\nMay still be loaded by\nother DLLs.\n\n" +
        "\u26A0 Consult mod page to\nverify version support if\nyou see this icon.\nSome DLLs' version support\ncannot be reliably\ndetermined.";

    /// <summary>The tree's columns: id, heading, width and whether the cells are right-aligned (<c>anchor=E</c>) rather than centred.</summary>
    public static readonly IReadOnlyList<(string Id, string Heading, int Width, bool RightAligned)> Columns =
    [
        ("#0", "DLL", 240, true),
        ("og", "OG", 60, false),
        ("ng", "NG", 60, false),
        ("ae", "AE", 60, false),
        ("user", "Your Game", 80, false),
    ];

    /// <summary>
    /// The legend's coloured runs, as the reference tags them: (line, first character, end character, palette key).
    /// Line 2's <c>Data/F4SE/Plugins/</c> and line 10's question mark are neutral-2; the glyphs opening lines 6, 8 and 14
    /// are good, independent and note.
    /// </summary>
    public static readonly IReadOnlyList<(int Line, int Start, int End, string ColourKey)> AboutRuns =
    [
        (2, 0, 18, "CmtNeutral2"),
        (6, 0, 1, "CmtGood"),
        (8, 0, 1, "CmtIndie"),
        (10, 0, 1, "CmtNeutral2"),
        (14, 0, 1, "CmtNote"),
    ];

    private const string Unknown = "\u2753";
    private const string Good = "\u2714";
    private const string Note = "\u26A0";
    private const string Independent = "\u2731";
    private const string Cross = "\u274C";

    /// <summary>
    /// The app font, falling back to Segoe UI Symbol for the glyphs: monochrome, so they take their run's colour as
    /// Tk's do. Without it Avalonia falls back to Segoe UI Emoji, whose colour glyphs ignore the tag colours.
    /// </summary>
    private static readonly FontFamily GlyphFont = new("avares://cm-toolkit/Assets/Fonts#Cascadia Mono, Segoe UI Symbol");

    private readonly AppRuntime _runtime;
    private readonly IHostEnvironment _host;
    private readonly GameInstall _game;
    private readonly OverviewState _overview;
    private IReadOnlyList<F4seDll> _dlls = [];

    /// <param name="runtime">The app runtime: its log and background operations.</param>
    /// <param name="host">The machine the plugins folder is on.</param>
    /// <param name="game">The installation found at startup, whose plugins folder is listed.</param>
    /// <param name="overview">The Overview's state, whose Install Type the "Your Game" column is for.</param>
    public F4seTab(AppRuntime runtime, IHostEnvironment host, GameInstall game, OverviewState overview)
        : base("F4SE", "F4SETab")
    {
        _runtime = runtime;
        _host = host;
        _game = game;
        _overview = overview;
    }

    /// <inheritdoc/>
    public override string? LoadingText => "Scanning DLLs...";

    /// <summary>The DLLs the load inspected, in directory order (<c>dll_info</c>); empty until it has loaded.</summary>
    public IReadOnlyList<F4seDll> Dlls => _dlls;

    /// <summary>The rows as built, in directory order; empty until the tab is built.</summary>
    public IReadOnlyList<F4seRow> Rows { get; private set; } = [];

    /// <summary>
    /// The glyph each verdict shows: an empty OG/NG/AE cell or a cross for "Your Game" when unsupported (F4SE-5, F4SE-6).
    /// </summary>
    /// <param name="support">The verdict.</param>
    /// <param name="yourGame">Whether it is the "Your Game" cell.</param>
    public static string Glyph(F4seSupport support, bool yourGame) => support switch
    {
        F4seSupport.Unknown => Unknown,
        F4seSupport.Supported => Good,
        F4seSupport.Note => Note,
        F4seSupport.Independent => Independent,
        F4seSupport.Unsupported => yourGame ? Cross : "",
        _ => throw new ArgumentOutOfRangeException(nameof(support), support, null),
    };

    /// <summary>The palette key of a row tag's colour (<c>tree_dlls.tag_configure</c>).</summary>
    public static string ColourKey(F4seRowTag tag) => tag switch
    {
        F4seRowTag.Neutral => "CmtNeutral1",
        F4seRowTag.Good => "CmtGood",
        F4seRowTag.Bad => "CmtBad",
        F4seRowTag.Note => "CmtNote",
        F4seRowTag.Independent => "CmtIndie",
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };

    /// <inheritdoc/>
    protected internal override async Task<bool> LoadAsync()
    {
        var scan = await _runtime.Operations.RunAsync("F4SE load", OperationPhase.Blocking, () => F4seScan.Run(_game, _host, _runtime.Logger));
        if (scan.LoadingError is { } error)
        {
            LoadingError = error;
            return false;
        }

        _dlls = scan.Dlls;
        return true;
    }

    /// <inheritdoc/>
    protected internal override Control BuildContent()
    {
        // The Install Type as it is now: the reference read cmc.game while building the tree, and never again.
        var installType = _overview.InstallType;
        Rows = [.. _dlls.Select(d => Row(d.Name, F4seVerdict.Of(d.Info, installType)))];

        // grid: column 0 (the tree) and 1 (its scrollbar) at their natural width, column 2 taking the rest; row 0 (the
        // heading) at its natural height, row 1 the rest. The tree and scrollbar span both rows.
        var table = BuildTree();
        // The Treeview's sv_ttk card field: the headings start 4 px in and 5 px down, and the card ends 5 px past the
        // last column (measured against the Tk pair).
        var tree = new Border { Child = table, Padding = new Thickness(4, 5, 5, 0), [Grid.RowSpanProperty] = 2 };
        var scrollBar = BuildScrollBar(table);

        // ttk.Label(font=FONT, anchor=N), grid(padx=5, pady=5) without sticky: centred in its cell.
        var heading = new TextBlock
        {
            Name = "Heading",
            Text = "F4SE DLLs",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(5),
            [Grid.ColumnProperty] = 2,
        };

        var about = BuildAbout();
        Grid.SetColumn(about, 2);
        Grid.SetRow(about, 1);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { tree, scrollBar, heading, about },
        };
    }

    /// <summary>
    /// The <c>ttk.Scrollbar</c> in its own column beside the tree (<c>command=tree_dlls.yview</c>,
    /// <c>yscrollcommand=scroll_tree_y.set</c>): always shown, the full height of the tab, and kept in step with the
    /// tree's rows both ways.
    /// </summary>
    /// <remarks>
    /// The TableView's own scrollbar is hidden: Fluent's ScrollViewer overlays it on the rows' right edge rather than
    /// giving it a column of its own, and it didn't show for a short list (the shell prototype's rough edge).
    /// </remarks>
    private static ScrollBar BuildScrollBar(TableView table)
    {
        // The sv_ttk trough is 12 px wide, 5 px right of the tree's card. Its thumb is the app's Fluent one, the full
        // width of the trough, where sv_ttk's is 6 px (a known residual, as for every scrollbar in the app).
        var bar = new ScrollBar
        {
            Name = "DllsScrollBar",
            Orientation = Orientation.Vertical,
            Visibility = ScrollBarVisibility.Visible,
            AllowAutoHide = false,
            Width = 12,
            // Fluent's bar is at least 16 px wide.
            MinWidth = 0,
            Margin = new Thickness(5, 0, 0, 0),
            [Grid.ColumnProperty] = 1,
            [Grid.RowSpanProperty] = 2,
        };

        table.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is not { } viewer)
            {
                return;
            }

            void Sync()
            {
                var viewport = viewer.Viewport.Height;
                bar.ViewportSize = viewport;
                bar.LargeChange = viewport;
                bar.Maximum = Math.Max(0, viewer.Extent.Height - viewport);
                bar.Value = viewer.Offset.Y;
            }

            viewer.ScrollChanged += (_, _) => Sync();
            viewer.PropertyChanged += (_, change) =>
            {
                if (change.Property == ScrollViewer.ExtentProperty || change.Property == ScrollViewer.ViewportProperty)
                {
                    Sync();
                }
            };
            // Setting the offset raises ScrollChanged, whose Sync sets the same value back: no loop.
            bar.ValueChanged += (_, change) => viewer.Offset = viewer.Offset.WithY(change.NewValue);
            Sync();
        };
        return bar;
    }

    /// <summary>One row: the verdicts' glyphs (three question marks for a non-plugin) and the tag.</summary>
    private static F4seRow Row(string name, F4seVerdict verdict)
    {
        IReadOnlyList<string> cells = verdict.YourGame is { } yourGame
            ? [Glyph(verdict.Og, false), Glyph(verdict.Ng, false), Glyph(verdict.Ae, false), Glyph(yourGame, true)]
            : [Unknown, Unknown, Unknown];
        return new F4seRow(name, cells, verdict.Tag);
    }

    /// <summary>
    /// The <c>ttk.Treeview</c>: five fixed-width columns under sv_ttk heading cells, and rows of 21 px (sv_ttk's
    /// <c>rowheight</c>), each coloured by its tag. Its scrollbar is <see cref="BuildScrollBar"/>'s.
    /// </summary>
    private TableView BuildTree()
    {
        var table = new TableView
        {
            Name = "Dlls",
            ItemsSource = Rows,
            CanUserResizeColumns = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
        };
        // Scrollable, but the bar shown is the separate one.
        ScrollViewer.SetVerticalScrollBarVisibility(table, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(table, ScrollBarVisibility.Disabled);
        table.Styles.Add(new Style(x => x.OfType<TableViewColumnHeader>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(ContentControl.VerticalContentAlignmentProperty, VerticalAlignment.Stretch),
            },
        });
        // The heading row's own strip: the sv_ttk headings are separate cells on the field, with no strip behind and no
        // padding round them. The strip lives in a nested template (the TableView's ScrollViewer's), which no style
        // selector here reached, so its two theme resources are overridden for this table instead; the rows' padding
        // is set by the style below, and the heading cells use their own brush.
        table.Resources["SystemControlBackgroundChromeMediumBrush"] = Brushes.Transparent;
        table.Resources["TableViewRowPadding"] = new Thickness(0);
        // Columns can't be resized here, so the resize grip's separator line goes too.
        table.Styles.Add(new Style(x => x.OfType<TableViewColumnHeader>().Template().OfType<Thumb>().Name("PART_Resizer"))
        {
            Setters = { new Setter(Visual.IsVisibleProperty, false) },
        });
        table.Styles.Add(new Style(x => x.OfType<TableViewCell>())
        {
            Setters =
            {
                new Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            },
        });
        // sv_ttk: rowheight = the body font's line space + 3, 21 px here; the 17 px cell text sits 4 px down (measured).
        table.Styles.Add(new Style(x => x.OfType<TableViewRow>())
        {
            Setters = { new Setter(TemplatedControl.PaddingProperty, new Thickness(0, 4, 0, 0)) },
        });

        for (var i = 0; i < Columns.Count; i++)
        {
            var (_, title, width, right) = Columns[i];
            var cell = i - 1;
            table.Columns.Add(new TableViewColumn
            {
                Header = title,
                HeaderTemplate = new FuncDataTemplate<string>((text, _) => Heading(text)),
                // Headers and cells take the column's alignment: stretched, so each lays its own text out.
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Width = new GridLength(width),
                CellTemplate = new FuncDataTemplate<F4seRow>((row, _) => Cell(row, cell, right)),
            });
        }

        return table;
    }

    /// <summary>
    /// One heading: sv_ttk's rounded heading cell, a shade lighter than the field and 2 px in from each column edge,
    /// 26 px tall, with the title centred in <c>FONT_SMALL</c> and <c>COLOR_DEFAULT</c> (<c>style.configure("Heading")</c>).
    /// </summary>
    private static Border Heading(string? title)
    {
        var text = new TextBlock
        {
            Text = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Classes.Add("small");
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("CmtDefault"));

        var cell = new Border
        {
            Child = text,
            Height = 26,
            Margin = new Thickness(2, 0),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        cell.Bind(Border.BackgroundProperty, cell.GetResourceObservable("SvHeadingFill"));
        cell.Bind(Border.BorderBrushProperty, cell.GetResourceObservable("SvHeadingEdge"));
        return cell;
    }

    /// <summary>
    /// One cell's text in its row's tag colour, clipped at the column's edge as a Treeview cell is. A name too wide for
    /// the right-aligned first column shows its start and loses its end, as Tk lays it out.
    /// </summary>
    private static Control Cell(F4seRow? row, int cell, bool right)
    {
        var text = new TextBlock
        {
            Text = row is null ? "" : cell < 0 ? row.Name : cell < row.Cells.Count ? row.Cells[cell] : "",
            // The text box sits at the column's right edge, its text left-aligned within: when the name is too wide, the
            // box is the column's width and its start shows, as in Tk.
            TextAlignment = right ? TextAlignment.Left : TextAlignment.Center,
            HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            // Fluent trims cell text with an ellipsis; a Treeview cell just cuts it off.
            TextTrimming = TextTrimming.None,
            // The glyphs are drawn in their row's colour, as Tk draws them, not as colour emoji.
            FontFamily = GlyphFont,
        };
        if (row is not null)
        {
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(ColourKey(row.Tag)));
        }

        return new Border
        {
            Child = text,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            // The #0 column keeps sv_ttk's 26 px Treeitem.indicator slot at its left, empty for rows without children,
            // which is where a name too wide to fit starts.
            Padding = new Thickness(cell < 0 ? 26 : 0, 0, 0, 0),
        };
    }

    /// <summary>
    /// The read-only <c>Text</c> holding <see cref="AboutText"/> at <c>FONT_SMALL</c>, with <see cref="AboutRuns"/>
    /// coloured. Its text can still be selected, as a disabled Tk Text's can.
    /// </summary>
    private static SelectableTextBlock BuildAbout()
    {
        // The tk.Text's border, highlight ring and padx/pady put its first character 8 px in and 2 px down (measured).
        var about = new SelectableTextBlock
        {
            Name = "About",
            TextWrapping = TextWrapping.Wrap,
            FontFamily = GlyphFont,
            Margin = new Thickness(7, 2, 0, 0),
        };
        about.Classes.Add("small");
        about.Bind(TextBlock.ForegroundProperty, about.GetResourceObservable("SvFg"));

        var lines = AboutText.Split('\n');
        for (var line = 0; line < lines.Length; line++)
        {
            if (line > 0)
            {
                about.Inlines!.Add(new LineBreak());
            }

            var text = lines[line];
            var start = 0;
            // Tk lines are numbered from 1; each line has at most one run here.
            foreach (var (_, first, end, key) in AboutRuns.Where(r => r.Line == line + 1))
            {
                if (first > start)
                {
                    about.Inlines!.Add(new Run(text[start..first]));
                }

                var run = new Run(text[first..end]);
                run.Bind(TextElement.ForegroundProperty, about.GetResourceObservable(key));
                about.Inlines!.Add(run);
                start = end;
            }

            if (start < text.Length)
            {
                about.Inlines!.Add(new Run(text[start..]));
            }
        }

        return about;
    }
}
