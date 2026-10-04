using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CMToolkit.App.Runtime;

namespace CMToolkit.App.Views;

/// <summary>One TreeWindow row: the first column's text and the second column's value.</summary>
/// <param name="Text">The key, formatted the way the consumer's reference did (<c>str(item[0])</c>).</param>
/// <param name="Value">The file name.</param>
public sealed record TreeWindowRow(string Text, string Value);

/// <summary>
/// <c>modal_window.TreeWindow</c> (Parity Inventory §9.2): an optional text, then a two-column list of keyed files with
/// a vertical scrollbar, then a Close button. Space closes it too. No row can be selected.
/// </summary>
public sealed class TreeWindow : ModalWindow
{
    /// <summary>The single row shown for an empty or missing list.</summary>
    private static readonly TreeWindowRow NoItems = new("No items to display.", "");

    private TreeWindow(
        AppRuntime runtime, int width, int height, string title, string text, IReadOnlyList<string> headers,
        IReadOnlyList<TreeWindowRow> rows)
        : base(runtime, title, width, height)
    {
        Rows = rows;

        var label = new TextBlock
        {
            Text = text,
            TextAlignment = TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        label.Classes.Add("small");

        var list = new StackPanel();
        foreach (var row in rows)
        {
            list.Children.Add(BuildRow(row));
        }

        var tree = new DockPanel();
        if (headers.Count > 0)
        {
            // show="tree headings" only when there are headers to show.
            tree.Children.Add(BuildHeadings(headers));
        }

        tree.Children.Add(new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // ttk.Scrollbar beside the tree is always there, whether or not the rows overflow.
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
        });

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
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                // anchor=W, wraplength=int(win_width * 0.9), padx=10, pady=10.
                new WraplengthPresenter { Wraplength = (int)(width * 0.9), Margin = new Thickness(10), Child = label },
                new Border { [Grid.RowProperty] = 1, Child = tree },
                // ttk.Button(width=win_width // 2) counts characters; grid shrinks it to the window less padx=10.
                new Border { [Grid.RowProperty] = 2, Child = close },
            },
        };

        CloseOnSpace();
    }

    /// <summary>The rows shown, in display order.</summary>
    public IReadOnlyList<TreeWindowRow> Rows { get; }

    /// <summary>
    /// Builds a TreeWindow over <paramref name="items"/>, sorted by key, highest first. The sort is stable, as Python's
    /// <c>sorted(reverse=True)</c> is, so equal keys keep their input order; string keys compare by character code, as
    /// Python's do, never by culture.
    /// </summary>
    /// <typeparam name="TKey">The key type: a count, a version or a string.</typeparam>
    /// <param name="runtime">The app runtime.</param>
    /// <param name="width">The client width; the text wraps at 90 % of it.</param>
    /// <param name="height">The client height.</param>
    /// <param name="title">The window title.</param>
    /// <param name="text">The text above the list; may be empty.</param>
    /// <param name="headers">The two column headings, or none for a list without a heading row.</param>
    /// <param name="items">Each key with the path whose file name is shown. Empty or <see langword="null"/> shows
    /// <c>No items to display.</c></param>
    /// <param name="keyText">Formats a key for the first column (the consumer's <c>str()</c>, T-3).</param>
    /// <param name="comparer">Orders the keys; defaults to ordinal for strings and the default comparer otherwise.</param>
    public static TreeWindow Create<TKey>(
        AppRuntime runtime, int width, int height, string title, string text, IReadOnlyList<string> headers,
        IReadOnlyList<(TKey Key, string Path)>? items, Func<TKey, string> keyText, IComparer<TKey>? comparer = null)
    {
        comparer ??= typeof(TKey) == typeof(string) ? (IComparer<TKey>)StringComparer.Ordinal : Comparer<TKey>.Default;
        IReadOnlyList<TreeWindowRow> rows = items is { Count: > 0 }
            ? [.. items.OrderByDescending(i => i.Key, comparer).Select(i => new TreeWindowRow(keyText(i.Key), Path.GetFileName(i.Path)))]
            : [NoItems];
        return new TreeWindow(runtime, width, height, title, text, headers, rows);
    }

    /// <summary>The heading row: <c>#0</c> centred, the value column left-aligned.</summary>
    private static Control BuildHeadings(IReadOnlyList<string> headers)
    {
        var headings = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("65,*"),
            [DockPanel.DockProperty] = Dock.Top,
        };
        // sv_ttk's heading sprite colour, shared with TableView headers in SvPalette.
        headings.Bind(Panel.BackgroundProperty, headings.GetResourceObservable("SystemControlBackgroundChromeMediumBrush"));
        for (var column = 0; column < headers.Count && column < 2; column++)
        {
            var heading = new TextBlock
            {
                Text = headers[column],
                HorizontalAlignment = column == 0 ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                Margin = new Thickness(4, 3),
                [Grid.ColumnProperty] = column,
            };
            heading.Classes.Add("small");
            heading.Classes.Add("heading");
            headings.Children.Add(heading);
        }

        return headings;
    }

    /// <summary>One row. Cells clip their text at the column edge, as Treeview cells do.</summary>
    private static Control BuildRow(TreeWindowRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("65,*") };
        grid.Children.Add(Cell(row.Text, 0));
        grid.Children.Add(Cell(row.Value, 1));
        return grid;

        static TextBlock Cell(string text, int column)
        {
            var cell = new TextBlock
            {
                Text = text,
                ClipToBounds = true,
                // ttk.Treeview rowheight is the font's line space plus 3 (~22 px; see SvPalette TableViewRowPadding).
                Padding = new Thickness(4, 2, 0, 3),
                [Grid.ColumnProperty] = column,
            };
            cell.Classes.Add("small");
            cell.Classes.Add("cell");
            return cell;
        }
    }
}
