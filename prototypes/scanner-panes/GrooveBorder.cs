using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CmtPanesPrototype;

/// <summary>
/// Tk <c>bd=2, relief=GROOVE</c> as the Python panes draw it: two 1 px rings, #555555 outside / #8E8E8E inside on
/// the top and left, reversed on the bottom and right (sampled from reference/python-scanner-composite.png).
/// Avalonia's Border takes one brush for all four sides, so this is drawn by hand.
/// </summary>
public sealed class GrooveBorder : Decorator
{
    private static readonly IPen Dark = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1);
    private static readonly IPen Light = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x8E)), 1);

    public GrooveBorder() => Padding = new Thickness(2);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        context.FillRectangle(new ImmutableSolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1C)), new Rect(Bounds.Size));

        // Lines sit on pixel centres (+0.5) so they stay 1 px crisp at 100 %.
        // Outer ring: dark top/left, light bottom/right.
        context.DrawLine(Dark, new Point(0, 0.5), new Point(w, 0.5));
        context.DrawLine(Dark, new Point(0.5, 0), new Point(0.5, h));
        context.DrawLine(Light, new Point(0, h - 0.5), new Point(w, h - 0.5));
        context.DrawLine(Light, new Point(w - 0.5, 0), new Point(w - 0.5, h));
        // Inner ring: light top/left, dark bottom/right.
        context.DrawLine(Light, new Point(1, 1.5), new Point(w - 1, 1.5));
        context.DrawLine(Light, new Point(1.5, 1), new Point(1.5, h - 1));
        context.DrawLine(Dark, new Point(1, h - 1.5), new Point(w - 1, h - 1.5));
        context.DrawLine(Dark, new Point(w - 1.5, 1), new Point(w - 1.5, h - 1));
    }
}
