using Avalonia;

namespace CMToolkit.App.Views;

/// <summary>Where the main window opens (SHELL-6).</summary>
public static class ShellPlacement
{
    /// <summary>The main window's client size in DIPs, which Tk pixels map to 1:1 (issue #13).</summary>
    public static readonly Size ClientSize = new(760, 450);

    /// <summary>
    /// The outer-window origin the Reference Implementation uses: <c>screen // 2 - client // 2</c> per axis, on the
    /// full screen size (not the work area), with the <b>client</b> size (not the frame), applied to the outer window
    /// rect. On Tk 8.6 at 100 % this put GetWindowRect's top-left at exactly that point. Avalonia's
    /// <c>CenterScreen</c> centres the outer frame in the work area instead, which lands 8 px left and 20 px higher.
    /// </summary>
    /// <param name="screenBounds">The primary screen's bounds, in physical pixels.</param>
    /// <param name="scaling">The screen's scale factor. The shipped reference is DPI-unaware, so it computes in
    /// logical (96-DPI) pixels and Windows scales the result back up; this does the same.</param>
    /// <returns>A value for <see cref="Avalonia.Controls.Window.Position"/>, which is the outer window rect's origin
    /// in physical pixels.</returns>
    /// <remarks>
    /// Only the 100 % case was measured. The rounding of a fractional scaled position (for example 495 x 1.5) is
    /// Windows' DPI virtualisation's, which wasn't observed, so this rounds half away from zero.
    /// </remarks>
    public static PixelPoint TkCentredOrigin(PixelRect screenBounds, double scaling)
    {
        var logicalWidth = (int)Math.Round(screenBounds.Width / scaling);
        var logicalHeight = (int)Math.Round(screenBounds.Height / scaling);
        // Python's // floors; these operands are non-negative, so C# integer division matches.
        var x = (logicalWidth / 2) - ((int)ClientSize.Width / 2);
        var y = (logicalHeight / 2) - ((int)ClientSize.Height / 2);
        return new PixelPoint(
            screenBounds.X + (int)Math.Round(x * scaling, MidpointRounding.AwayFromZero),
            screenBounds.Y + (int)Math.Round(y * scaling, MidpointRounding.AwayFromZero));
    }
}
