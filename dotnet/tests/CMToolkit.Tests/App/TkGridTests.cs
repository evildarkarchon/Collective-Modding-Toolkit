using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CMToolkit.App.Tabs;

namespace CMToolkit.Tests.App;

/// <summary>
/// <see cref="TkGrid"/>, Tk's <c>grid</c> sizing. The expected numbers were measured from the Reference
/// Implementation's Tools and About tabs (Tk 8.6, the 758x407 tab page).
/// </summary>
public sealed class TkGridTests
{
    [Fact]
    public void Extra_space_is_shared_by_weight_with_the_remainder_on_the_later_slots()
    {
        // The Tools tab: 11 px spare over three weight-1 columns went 3, 4, 4.
        double[] sizes = [195, 280, 272];
        TkGrid.Distribute(sizes, [1, 1, 1], 11);
        Assert.Equal([198, 284, 276], sizes);
    }

    [Fact]
    public void A_shortfall_is_taken_the_same_way_truncating_toward_zero()
    {
        double[] sizes = [100, 286];
        TkGrid.Distribute(sizes, [1, 1], -13);
        Assert.Equal([94, 279], sizes);
    }

    [Fact]
    public void Slots_without_weight_get_nothing_and_with_no_weight_at_all_nothing_moves()
    {
        double[] weighted = [10, 10, 10];
        TkGrid.Distribute(weighted, [0, 1, 0], 9);
        Assert.Equal([10, 19, 10], weighted);

        double[] none = [10, 10];
        TkGrid.Distribute(none, [0, 0], 9);
        Assert.Equal([10, 10], none);
    }

    [AvaloniaFact]
    public void A_spanning_child_short_of_room_grows_its_weighted_rows_then_the_rest_is_shared()
    {
        // The About tab: a 70+20 px title in row 0, a 256+20 px icon in row 1, both rows weight 1, and a 386 px text
        // frame spanning both. Its 20 px shortfall went 10/10, then the page's 21 spare px 10/11: rows 110 and 297.
        var title = new Border { MinHeight = 90 };
        var icon = new Border { MinHeight = 276, [Grid.RowProperty] = 1 };
        var text = new Border { MinHeight = 386, [Grid.ColumnProperty] = 1, [Grid.RowSpanProperty] = 2 };
        var grid = new TkGrid { ColumnWeights = [1, 0], RowWeights = [1, 1], Children = { title, icon, text } };
        var window = new Window { Width = 758, Height = 407, Content = grid };
        window.Show();

        Assert.Equal(new Rect(0, 0, 758, 110), title.Bounds);
        Assert.Equal(new Rect(0, 110, 758, 297), icon.Bounds);
    }

    [AvaloniaFact]
    public void A_spanning_child_short_of_room_over_unweighted_rows_shares_the_shortfall_evenly()
    {
        // The Downgrader: the About button (pady=(5, 0), 30 px) alone in row 2, and the 90 px Current Creation Kit and
        // Options frames spanning rows 2-3, no weights. Its 55 px shortfall went 27/28: About's row was 62 px, so the
        // button, centred in it, sat 13 px below its pady (measured from the Tk window).
        var about = new Border { MinHeight = 35 };
        var below = new Border { [Grid.RowProperty] = 1 };
        var frame = new Border { MinHeight = 90, [Grid.ColumnProperty] = 1, [Grid.RowSpanProperty] = 2 };
        var grid = new TkGrid { Children = { about, below, frame } };
        var window = new Window { Width = 600, Height = 334, Content = grid };
        window.Show();

        Assert.Equal(62, about.Bounds.Height);
        Assert.Equal(new Rect(0, 62, 0, 28), below.Bounds);
    }

    [AvaloniaFact]
    public void A_column_pad_is_added_to_the_column_and_given_to_a_child_that_fills_it()
    {
        var child = new Border { Width = double.NaN, MinWidth = 180, Margin = new Thickness(5) };
        var grid = new TkGrid { ColumnPads = [5] };
        grid.Children.Add(child);
        grid.Measure(Size.Infinity);

        Assert.Equal(195, grid.DesiredSize.Width);
    }
}
