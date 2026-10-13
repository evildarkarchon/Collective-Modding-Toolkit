using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CMToolkit.App.Views;

namespace CMToolkit.Tests.App;

/// <summary>
/// The Scanner's docked panes (SCN-3, SCN-4, SCN-5): borderless owned windows at the Tk <c>update_geometry</c> offsets
/// from the main window's client origin, following it when it moves. The real-desktop checks (title-bar drag, minimise,
/// other monitors and DPIs) are <c>eng/Test-ScannerPanes.ps1</c>'s.
/// </summary>
public sealed class OwnedPaneDockerTests
{
    [AvaloniaFact]
    [Trait("Parity", "SCN-3")]
    public void The_side_pane_is_200_wide_right_of_the_client_area_40_down_and_45_shorter()
    {
        var owner = Owner();
        using var docker = new OwnedPaneDocker(owner);

        var pane = docker.Show(PaneSlot.Side, new Border());

        var origin = owner.PointToScreen(default);
        Assert.Equal(new PixelRect(origin.X + 760, origin.Y + 40, 200, 405), Rect(pane));
        Assert.Same(owner, pane.Owner);
        Assert.Equal(WindowDecorations.None, pane.WindowDecorations);
        Assert.False(pane.ShowInTaskbar);
        Assert.False(pane.CanResize);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-4")]
    public void The_details_pane_is_the_client_width_and_200_tall_directly_below_it()
    {
        var owner = Owner();
        using var docker = new OwnedPaneDocker(owner);

        var pane = docker.Show(PaneSlot.Details, new Border());

        var origin = owner.PointToScreen(default);
        Assert.Equal(new PixelRect(origin.X, origin.Y + 450, 760, 200), Rect(pane));
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-5")]
    public void Both_panes_follow_the_main_window_when_it_moves()
    {
        var owner = Owner();
        using var docker = new OwnedPaneDocker(owner);
        var side = docker.Show(PaneSlot.Side, new Border());
        var details = docker.Show(PaneSlot.Details, new Border());

        owner.Position += new PixelPoint(137, 61);

        var origin = owner.PointToScreen(default);
        Assert.Equal(new PixelPoint(origin.X + 760, origin.Y + 40), side.Position);
        Assert.Equal(new PixelPoint(origin.X, origin.Y + 450), details.Position);
    }

    [AvaloniaFact]
    [Trait("Parity", "SCN-5")]
    public void Closing_a_pane_closes_its_window_and_disposing_closes_the_rest()
    {
        var owner = Owner();
        var docker = new OwnedPaneDocker(owner);
        var side = docker.Show(PaneSlot.Side, new Border());
        var details = docker.Show(PaneSlot.Details, new Border());
        var closed = new List<PaneSlot>();
        side.Closed += (_, _) => closed.Add(PaneSlot.Side);
        details.Closed += (_, _) => closed.Add(PaneSlot.Details);

        docker.Close(PaneSlot.Details);
        Assert.Equal([PaneSlot.Details], closed);
        Assert.False(docker.Panes.ContainsKey(PaneSlot.Details));

        docker.Dispose();
        Assert.Equal([PaneSlot.Details, PaneSlot.Side], closed);
        Assert.Empty(docker.Panes);
    }

    private static Window Owner()
    {
        var owner = new Window { Width = 760, Height = 450, Position = new PixelPoint(100, 100) };
        owner.Show();
        return owner;
    }

    private static PixelRect Rect(Window pane)
        => new(pane.Position, new PixelSize((int)Math.Round(pane.Width * pane.DesktopScaling), (int)Math.Round(pane.Height * pane.DesktopScaling)));
}
