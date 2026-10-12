using Avalonia;
using Avalonia.Controls;

namespace CMToolkit.App.Tabs;

/// <summary>
/// A panel that sizes its rows and columns the way Tk's <c>grid</c> geometry manager does, where an Avalonia
/// <see cref="Grid"/> would not: a slot with weight starts at its content's size and then takes a weight-proportional
/// share of the space left over (or gives one up), whereas a star row or column ignores its content. Children are
/// placed with <see cref="Grid"/>'s attached <c>Row</c>/<c>Column</c>/<c>RowSpan</c>/<c>ColumnSpan</c>; a child's
/// <c>Margin</c> is its <c>padx</c>/<c>pady</c> and its alignment its <c>sticky</c> (Stretch is sticky to both sides).
/// </summary>
/// <remarks>
/// Reproduces what the Tools and About tabs rely on: <c>grid_columnconfigure(i, weight=w, pad=p)</c>,
/// <c>grid_rowconfigure(i, weight=w)</c>, and a child spanning slots. Not modelled: <c>minsize</c>, <c>uniform</c>,
/// and slots shrinking below their content when the panel is too small for it to.
/// </remarks>
public sealed class TkGrid : Panel
{
    /// <summary>Each column's <c>weight</c>; a column without an entry has weight 0.</summary>
    public IReadOnlyList<int> ColumnWeights { get; init; } = [];

    /// <summary>Each row's <c>weight</c>; a row without an entry has weight 0.</summary>
    public IReadOnlyList<int> RowWeights { get; init; } = [];

    /// <summary>
    /// Each column's <c>pad</c>: added to the column's size, and so given to a child that stretches across it.
    /// </summary>
    public IReadOnlyList<double> ColumnPads { get; init; } = [];

    /// <summary>
    /// Shares <paramref name="extra"/> out over <paramref name="sizes"/> in proportion to <paramref name="weights"/>,
    /// as Tk's <c>AdjustOffsets</c> does: each slot's end moves by <c>extra * (weights so far) / (total weight)</c>,
    /// truncated, so the remainder lands on the later slots. With no weight at all nothing moves.
    /// </summary>
    /// <param name="sizes">The slot sizes, changed in place.</param>
    /// <param name="weights">The slot weights, as long as <paramref name="sizes"/>.</param>
    /// <param name="extra">The whole number of pixels to add; negative to take away.</param>
    public static void Distribute(double[] sizes, IReadOnlyList<int> weights, double extra)
    {
        var total = weights.Sum();
        if (total == 0 || extra == 0)
        {
            return;
        }

        var cumulative = 0;
        var given = 0.0;
        for (var i = 0; i < sizes.Length; i++)
        {
            cumulative += weights[i];
            // C integer division truncates toward zero, for a shrink as for a grow.
            var end = Math.Truncate(extra * cumulative / total);
            sizes[i] += end - given;
            given = end;
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
        }

        return new Size(Resolve(columns: true).Sum(), Resolve(columns: false).Sum());
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = Resolve(columns: true);
        var heights = Resolve(columns: false);
        Distribute(widths, Weights(ColumnWeights, widths.Length), Math.Round(finalSize.Width - widths.Sum()));
        Distribute(heights, Weights(RowWeights, heights.Length), Math.Round(finalSize.Height - heights.Sum()));

        foreach (var child in Children)
        {
            var (column, columnSpan) = (Grid.GetColumn(child), Grid.GetColumnSpan(child));
            var (row, rowSpan) = (Grid.GetRow(child), Grid.GetRowSpan(child));
            child.Arrange(new Rect(
                widths.Take(column).Sum(),
                heights.Take(row).Sum(),
                widths.Skip(column).Take(columnSpan).Sum(),
                heights.Skip(row).Take(rowSpan).Sum()));
        }

        return finalSize;
    }

    /// <summary>
    /// The slot sizes the children ask for along one axis: each slot fits its single-slot children (plus the column's
    /// pad), then a spanning child short of room gets the shortfall shared over its slots by weight, or all of it on
    /// its last slot when they have none.
    /// </summary>
    private double[] Resolve(bool columns)
    {
        var count = Children.Count == 0 ? 0 : Children.Max(c => columns
            ? Grid.GetColumn(c) + Grid.GetColumnSpan(c)
            : Grid.GetRow(c) + Grid.GetRowSpan(c));
        var weights = Weights(columns ? ColumnWeights : RowWeights, count);
        var sizes = new double[count];

        foreach (var child in Children)
        {
            var (start, span) = Slot(child, columns);
            if (span == 1)
            {
                var pad = columns && start < ColumnPads.Count ? ColumnPads[start] : 0;
                sizes[start] = Math.Max(sizes[start], Desired(child, columns) + pad);
            }
        }

        foreach (var child in Children)
        {
            var (start, span) = Slot(child, columns);
            var shortfall = Desired(child, columns) - sizes.Skip(start).Take(span).Sum();
            if (span > 1 && shortfall > 0)
            {
                var spanned = sizes[start..(start + span)];
                var spannedWeights = weights.Skip(start).Take(span).ToArray();
                if (spannedWeights.Sum() == 0)
                {
                    spanned[^1] += shortfall;
                }
                else
                {
                    Distribute(spanned, spannedWeights, shortfall);
                }

                spanned.CopyTo(sizes, start);
            }
        }

        return sizes;
    }

    private static (int Start, int Span) Slot(Control child, bool columns) => columns
        ? (Grid.GetColumn(child), Grid.GetColumnSpan(child))
        : (Grid.GetRow(child), Grid.GetRowSpan(child));

    private static double Desired(Control child, bool columns) => columns ? child.DesiredSize.Width : child.DesiredSize.Height;

    private static int[] Weights(IReadOnlyList<int> configured, int count)
        => [.. Enumerable.Range(0, count).Select(i => i < configured.Count ? configured[i] : 0)];
}
