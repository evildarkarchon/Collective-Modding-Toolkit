using Avalonia;
using Avalonia.Controls;

namespace CMToolkit.App.Tabs;

/// <summary>
/// Tk's <c>pack(side=LEFT, fill=BOTH, expand=True)</c> for every child, as the Overview packs its three label frames
/// (OVW-1): each child gets its natural width plus an equal share of the width left over, and the full height. A Grid's
/// star columns would split the whole width equally instead, which gives the frames the wrong widths (the main window
/// prototype, issue #9).
/// </summary>
public sealed class PackRow : Panel
{
    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Tk's XExpansion, in whole pixels: each child takes the floor of the space still left over divided by the
        // children still to place, so the remainder goes to the later ones.
        var remaining = Math.Floor(Math.Max(0, finalSize.Width - Children.Sum(c => c.DesiredSize.Width)));
        double x = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var share = Math.Floor(remaining / (Children.Count - i));
            remaining -= share;
            var width = child.DesiredSize.Width + share;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
