using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CMToolkit.App.Views;

/// <summary>
/// Lays a text child out the way a <c>ttk.Label</c> with a <c>wraplength</c> does: the text wraps at
/// <see cref="Wraplength"/>, which need not match the space the label actually gets, and the resulting block is placed by
/// the child's <see cref="Layoutable.HorizontalAlignment"/> (Tk's <c>anchor</c>), overhanging and clipped if it is wider.
/// </summary>
/// <remarks>
/// A plain TextBlock wraps at whatever width its parent offers, so a modal whose label is padded 10 px each side would
/// wrap 20 px early. Tk lays the text out at the wraplength first and only then fits the block into the label.
/// </remarks>
public sealed class WraplengthPresenter : Decorator
{
    /// <summary>Defines the <see cref="Wraplength"/> property.</summary>
    public static readonly StyledProperty<double> WraplengthProperty =
        AvaloniaProperty.Register<WraplengthPresenter, double>(nameof(Wraplength), double.PositiveInfinity);

    static WraplengthPresenter()
    {
        AffectsMeasure<WraplengthPresenter>(WraplengthProperty);
        ClipToBoundsProperty.OverrideDefaultValue<WraplengthPresenter>(true);
    }

    /// <summary>The width the text wraps at, in Tk pixels (DIPs).</summary>
    public double Wraplength
    {
        get => GetValue(WraplengthProperty);
        set => SetValue(WraplengthProperty, value);
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            return default;
        }

        Child.Measure(new Size(Wraplength, double.PositiveInfinity));
        return new Size(Math.Min(Child.DesiredSize.Width, availableSize.Width), Child.DesiredSize.Height);
    }

    /// <inheritdoc/>
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is null)
        {
            return finalSize;
        }

        // The block keeps its natural (wrapped) width even when that is wider than the label.
        var width = Child.DesiredSize.Width;
        var x = Child.HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (finalSize.Width - width) / 2,
            HorizontalAlignment.Right => finalSize.Width - width,
            _ => 0,
        };
        Child.Arrange(new Rect(x, 0, width, Child.DesiredSize.Height));
        return finalSize;
    }
}
