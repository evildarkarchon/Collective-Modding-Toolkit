using Avalonia;
using CMToolkit.App.Views;

namespace CMToolkit.Tests.App;

/// <summary>
/// SHELL-6: <c>x = screenwidth // 2 - 760 // 2</c>, <c>y = screenheight // 2 - 450 // 2</c>, then
/// <c>wm_geometry("760x450+x+y")</c>. Measured on Tk 8.6 (Windows 11, 2560x1440 at 100 %): that puts the outer
/// window rect (GetWindowRect, invisible resize border included) at exactly (900, 495). Avalonia's CenterScreen put
/// the same rect at (892, 475), because it centres the outer frame in the work area rather than offsetting the
/// client size from the full screen.
/// </summary>
[Trait("Parity", "SHELL-6")]
public sealed class ShellPlacementTests
{
    [Fact]
    public void At_100_percent_the_outer_rect_starts_where_tk_puts_it()
    {
        var origin = ShellPlacement.TkCentredOrigin(new PixelRect(0, 0, 2560, 1440), scaling: 1.0);

        Assert.Equal(new PixelPoint(900, 495), origin);
    }

    [Fact]
    public void Odd_screen_sizes_floor_like_python_integer_division()
    {
        // 1365 // 2 - 380 = 302, 767 // 2 - 225 = 158.
        var origin = ShellPlacement.TkCentredOrigin(new PixelRect(0, 0, 1365, 767), scaling: 1.0);

        Assert.Equal(new PixelPoint(302, 158), origin);
    }

    [Fact]
    public void Above_100_percent_the_formula_runs_in_the_logical_pixels_a_dpi_unaware_tk_sees()
    {
        // The shipped reference is DPI-unaware (issue #13): at 150 % a 3840x2160 screen reads as 2560x1440, and
        // Windows scales the logical position back up, so the window lands at (900, 495) x 1.5.
        var origin = ShellPlacement.TkCentredOrigin(new PixelRect(0, 0, 3840, 2160), scaling: 1.5);

        Assert.Equal(new PixelPoint(1350, 743), origin);
    }

    [Fact]
    public void The_position_is_relative_to_the_screens_own_origin()
    {
        var origin = ShellPlacement.TkCentredOrigin(new PixelRect(-2560, 100, 2560, 1440), scaling: 1.0);

        Assert.Equal(new PixelPoint(-1660, 595), origin);
    }
}
