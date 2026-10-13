using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CMToolkit.App.Views;

/// <summary>
/// Tk's <c>bd=2, relief=GROOVE</c>, as the Scanner panes draw it: two 1 px rings, <c>SvSunkenDark</c> (#555555)
/// outside and <c>SvSunkenLight</c> (#8E8E8E) inside on the top and left, the other way round on the bottom and right,
/// round the theme background (<c>SvBg</c>). Measured from the reference at 100 % by the panes prototype (issue #12).
/// </summary>
/// <remarks>Avalonia's <see cref="Border"/> takes one brush for all four sides, so the rings are drawn by hand.</remarks>
public sealed class GrooveBorder : Decorator
{
    /// <summary>Insets the content by the border's 2 px.</summary>
    public GrooveBorder() => Padding = new Thickness(2);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var dark = new ImmutablePen(Palette("SvSunkenDark"), 1);
        var light = new ImmutablePen(Palette("SvSunkenLight"), 1);
        context.FillRectangle(Palette("SvBg"), new Rect(Bounds.Size));

        // Lines sit on pixel centres (+0.5) so they stay 1 px crisp at 100 %.
        // Outer ring: dark top/left, light bottom/right.
        context.DrawLine(dark, new Point(0, 0.5), new Point(w, 0.5));
        context.DrawLine(dark, new Point(0.5, 0), new Point(0.5, h));
        context.DrawLine(light, new Point(0, h - 0.5), new Point(w, h - 0.5));
        context.DrawLine(light, new Point(w - 0.5, 0), new Point(w - 0.5, h));
        // Inner ring: light top/left, dark bottom/right.
        context.DrawLine(light, new Point(1, 1.5), new Point(w - 1, 1.5));
        context.DrawLine(light, new Point(1.5, 1), new Point(1.5, h - 1));
        context.DrawLine(dark, new Point(1, h - 1.5), new Point(w - 1, h - 1.5));
        context.DrawLine(dark, new Point(w - 1.5, 1), new Point(w - 1.5, h - 1));
    }

    /// <summary>A colour of the sv palette (<c>Theme/SvPalette.axaml</c>), as an immutable brush for drawing.</summary>
    /// <exception cref="InvalidOperationException">The key isn't a solid colour brush reachable from this control.</exception>
    private ImmutableSolidColorBrush Palette(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? new ImmutableSolidColorBrush(brush.Color)
            : throw new InvalidOperationException($"The palette has no colour {key}.");
}
