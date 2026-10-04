using Avalonia;
using Avalonia.Controls;

namespace CmtShellPrototype;

/// <summary>
/// Tk's <c>pack(side=LEFT, fill=BOTH, expand=True)</c> for every child: each child gets its natural width plus an
/// equal share of the leftover width, and the full height. Grid's star columns split the <em>whole</em> width
/// equally instead, which makes the three Overview boxes the wrong widths - hence this panel.
/// </summary>
public sealed class PackRow : Panel
{
    /// <inheritdoc />
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

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var natural = Children.Sum(c => c.DesiredSize.Width);
        var extra = Children.Count == 0 ? 0 : Math.Max(0, finalSize.Width - natural) / Children.Count;
        double x = 0;
        foreach (var child in Children)
        {
            var w = child.DesiredSize.Width + extra;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w;
        }

        return finalSize;
    }
}
