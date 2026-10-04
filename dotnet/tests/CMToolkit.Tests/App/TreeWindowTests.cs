using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using CMToolkit.App.Runtime;
using CMToolkit.App.Views;
using CMToolkit.Tests.Support;

namespace CMToolkit.Tests.App;

/// <summary><c>modal_window.TreeWindow</c> (Parity Inventory §9.2), the generic list modal its consumers open later.</summary>
public sealed class TreeWindowTests
{
    private static (AppRuntime Runtime, Window Owner) ShowOwner()
    {
        var runtime = new AppRuntime(new CapturingLogger());
        var owner = new Window { Width = 760, Height = 450 };
        owner.Show();
        return (runtime, owner);
    }

    private static TreeWindow ShowTree<TKey>(
        IReadOnlyList<(TKey Key, string Path)>? items,
        Func<TKey, string> keyText,
        IReadOnlyList<string>? headers = null,
        string text = "Some modules have a bad header version.")
    {
        var (runtime, owner) = ShowOwner();
        var tree = TreeWindow.Create(
            runtime, 400, 500, "Detected Invalid Module Versions", text, headers ?? ["HEDR", "Module"], items, keyText);
        _ = tree.ShowModalAsync(owner);
        return tree;
    }

    private static IReadOnlyList<string> Cells(TreeWindow tree)
        => [.. tree.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("cell")).Select(t => t.Text ?? "")];

    [AvaloniaFact]
    public void Rows_show_the_key_and_the_file_name_sorted_by_key_descending()
    {
        var tree = ShowTree<double>(
            [(0.94, @"C:\Game\Data\Fallout3Leftover.esp"), (1.71, @"C:\Game\Data\SkyrimPort.esp"), (1.7, @"C:\Game\Data\OldSkyrimThing.esm")],
            key => key.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(
            [new("1.71", "SkyrimPort.esp"), new("1.7", "OldSkyrimThing.esm"), new TreeWindowRow("0.94", "Fallout3Leftover.esp")],
            tree.Rows);
        Assert.Equal(["1.71", "SkyrimPort.esp", "1.7", "OldSkyrimThing.esm", "0.94", "Fallout3Leftover.esp"], Cells(tree));
    }

    [AvaloniaFact]
    public void Equal_keys_keep_their_original_order()
    {
        // sorted(..., reverse=True) is stable: ties stay in input order rather than being reversed.
        var tree = ShowTree<int>([(1, "a.esp"), (2, "b.esp"), (1, "c.esp"), (2, "d.esp")], key => key.ToString());

        Assert.Equal(["b.esp", "d.esp", "a.esp", "c.esp"], tree.Rows.Select(r => r.Value));
    }

    [AvaloniaFact]
    public void String_keys_sort_by_code_point_not_by_culture()
    {
        var tree = ShowTree<string>([("a", "1.esp"), ("B", "2.esp"), ("b", "3.esp")], key => key);

        Assert.Equal(["b", "a", "B"], tree.Rows.Select(r => r.Text));
    }

    [AvaloniaFact]
    public void An_empty_or_missing_list_shows_no_items_to_display()
    {
        var empty = ShowTree<int>([], key => key.ToString());
        var missing = ShowTree<int>(null, key => key.ToString());

        Assert.Equal([new TreeWindowRow("No items to display.", "")], empty.Rows);
        Assert.Equal([new TreeWindowRow("No items to display.", "")], missing.Rows);
    }

    [AvaloniaFact]
    public void The_first_column_is_65px_with_a_centred_heading_and_the_second_stretches_with_a_left_heading()
    {
        var tree = ShowTree<int>([(1, "a.esp")], key => key.ToString());

        var headings = tree.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("heading")).ToList();
        Assert.Equal(["HEDR", "Module"], headings.Select(h => h.Text));
        Assert.Equal(HorizontalAlignment.Center, headings[0].HorizontalAlignment);
        Assert.Equal(HorizontalAlignment.Left, headings[1].HorizontalAlignment);

        var firstCell = tree.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("cell"));
        var row = Assert.IsType<Grid>(firstCell.Parent);
        Assert.Equal(new GridLength(65), row.ColumnDefinitions[0].Width);
        Assert.True(row.ColumnDefinitions[1].Width.IsStar);
    }

    [AvaloniaFact]
    public void Without_headers_there_is_no_heading_row()
    {
        var tree = ShowTree<int>([(1, "a.esp")], key => key.ToString(), headers: []);

        Assert.DoesNotContain(tree.GetVisualDescendants().OfType<TextBlock>(), t => t.Classes.Contains("heading"));
    }

    [AvaloniaFact]
    public void The_text_wraps_at_90_percent_of_the_window_width_at_10pt()
    {
        var tree = ShowTree<int>([(1, "a.esp")], key => key.ToString(), text: "Explanation");

        var label = tree.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Explanation");
        var presenter = Assert.IsType<WraplengthPresenter>(label.Parent);
        Assert.Equal(360, presenter.Wraplength);
        Assert.Equal(13, label.FontSize);
        Assert.Equal(new Point(10, 10), label.TranslatePoint(default, tree));
    }

    [AvaloniaFact]
    [Trait("Parity", "MOD-1")]
    public void The_tree_window_is_a_modal_that_closes_on_space_escape_or_close()
    {
        var bySpace = ShowTree<int>([(1, "a.esp")], key => key.ToString());
        Assert.False(bySpace.CanResize);
        Assert.Equal(new Size(400, 500), bySpace.ClientSize);
        bySpace.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        Assert.False(bySpace.IsVisible);

        var byEscape = ShowTree<int>([(1, "a.esp")], key => key.ToString());
        byEscape.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Assert.False(byEscape.IsVisible);

        var byButton = ShowTree<int>([(1, "a.esp")], key => key.ToString());
        var close = byButton.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close"));
        var centre = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), byButton)!.Value;
        byButton.MouseDown(centre, MouseButton.Left);
        byButton.MouseUp(centre, MouseButton.Left);
        Assert.False(byButton.IsVisible);
    }
}
