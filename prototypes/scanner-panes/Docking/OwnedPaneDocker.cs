using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CmtPanesPrototype.Docking;

/// <summary>Which edge a docked pane hangs off.</summary>
public enum PaneSlot
{
    /// <summary>SidePane: right of the client area, 40 below its top, 5 short of its bottom, 200 wide.</summary>
    Side,

    /// <summary>ResultDetailsPane: under the client area, full client width, 200 tall.</summary>
    Details,
}

/// <summary>
/// A borderless, taskbar-less window owned by the main window: the Avalonia stand-in for the Python panes'
/// <c>Toplevel(...).wm_overrideredirect(True)</c>.
/// </summary>
public sealed class PaneWindow : Window
{
    private readonly LayoutTransformControl _host = new();

    public PaneWindow(PaneSlot slot, Control content)
    {
        Slot = slot;
        Title = $"Collective Modding Toolkit pane ({slot})";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        _host.Child = content;
        Content = _host;
    }

    public PaneSlot Slot { get; }

    /// <summary>The pane's view, without the counter-scaling wrapper.</summary>
    public Control? View
    {
        get => _host.Child;
        set => _host.Child = value;
    }

    /// <summary>
    /// Lays the view out in the owner's DIPs. When the pane sits on a monitor with another DPI than its owner (it
    /// straddles two monitors), Windows gives it that monitor's scaling, so 200 physical px would be only 133 DIPs
    /// at 150 % and the content would render 1.5x too big for its box. Scaling by owner/pane undoes that; text is
    /// still rasterised at the pane's own density, so it stays sharp.
    /// </summary>
    public void MatchOwnerScale(double ownerScaling)
    {
        var k = ownerScaling / RenderScaling;
        _host.LayoutTransform = Math.Abs(k - 1) < 0.001 ? null : new ScaleTransform(k, k);
    }
}

/// <summary>
/// Variant A. Keeps the pane windows glued to the main window, re-placing them on every move, resize and DPI
/// change, which is what the Python <c>&lt;Configure&gt;</c> handler (<c>ScannerTab.on_configure</c>) does.
/// </summary>
/// <remarks>
/// The geometry is computed the way the DPI-baseline ticket prescribes: Tk works in one space (logical pixels from
/// the root's client origin), but Avalonia's <c>Window.Position</c> is the outer rect in physical pixels while
/// <c>Width</c>/<c>Height</c> are DIPs of the window's own monitor. So the rect is anchored on
/// <c>PointToScreen(0,0)</c>, scaled by the owner's scaling to physical pixels, and the pane's size is then
/// converted back to DIPs with the pane's own <c>DesktopScaling</c>, which differs from the owner's when the pane
/// lands on a monitor with another DPI.
/// <para>Minimise/restore is deliberately not handled: Win32 hides and re-shows owned windows with their owner,
/// which replaces <c>CMChecker.on_minimize/on_restore</c>. The self-test checks that this holds.</para>
/// </remarks>
public sealed class OwnedPaneDocker : IDisposable
{
    private readonly Window _owner;
    private readonly Dictionary<PaneSlot, PaneWindow> _panes = [];
    private bool _syncing;

    public OwnedPaneDocker(Window owner)
    {
        _owner = owner;
        _owner.PositionChanged += OnOwnerChanged;
        _owner.Resized += OnOwnerChanged;
        _owner.ScalingChanged += OnOwnerChanged;
    }

    /// <summary>Number of <see cref="Sync"/> passes so far; surfaced in the readout to show how often placement runs.</summary>
    public int SyncCount { get; private set; }

    public IReadOnlyDictionary<PaneSlot, PaneWindow> Panes => _panes;

    /// <summary>Creates (or reuses) the pane window for <paramref name="slot"/>, shows it owned by the main window and places it.</summary>
    public void Show(PaneSlot slot, Control content)
    {
        if (_panes.TryGetValue(slot, out var existing))
        {
            existing.View = content;
            return;
        }

        var pane = new PaneWindow(slot, content);
        _panes[slot] = pane;
        // A pane that crosses onto a monitor with another DPI gets WM_DPICHANGED and Windows resizes it to the
        // suggested rect; re-place it so its physical size matches the owner again.
        pane.ScalingChanged += (_, _) => Sync();
        Place(pane);
        pane.Show(_owner);
        Place(pane);
    }

    /// <summary>Closes the pane for <paramref name="slot"/> if it exists (Tk: <c>pane.destroy()</c>).</summary>
    public void Close(PaneSlot slot)
    {
        if (_panes.Remove(slot, out var pane))
        {
            pane.Close();
        }
    }

    /// <summary>Re-places every open pane against the owner's current client rect.</summary>
    public void Sync()
    {
        // Setting Position/Size on a pane can raise its own ScalingChanged synchronously; one pass is enough.
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            SyncCount++;
            foreach (var pane in _panes.Values)
            {
                Place(pane);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// Where a pane should be, in physical screen pixels: the Tk <c>update_geometry</c> formulas, evaluated in the
    /// owner's DIPs and scaled by the owner's render scaling. Public so the readout can compare it to the real rect.
    /// </summary>
    public PixelRect Expected(PaneSlot slot)
    {
        var origin = _owner.PointToScreen(default);
        var s = _owner.RenderScaling;
        var client = _owner.ClientSize;
        var dip = slot switch
        {
            // SidePane.update_geometry: f"{200}x{root_height - 40 - 5}+{root_x + root_width}+{root_y + 40}"
            PaneSlot.Side => new Rect(client.Width, 40, 200, client.Height - 40 - 5),
            // ResultDetailsPane.update_geometry: f"{root_width}x{200}+{root_x}+{root_y + root_height}"
            _ => new Rect(0, client.Height, client.Width, 200),
        };
        return new PixelRect(
            origin.X + (int)Math.Round(dip.X * s),
            origin.Y + (int)Math.Round(dip.Y * s),
            (int)Math.Round(dip.Width * s),
            (int)Math.Round(dip.Height * s));
    }

    public void Dispose()
    {
        _owner.PositionChanged -= OnOwnerChanged;
        _owner.Resized -= OnOwnerChanged;
        _owner.ScalingChanged -= OnOwnerChanged;
        foreach (var slot in _panes.Keys.ToList())
        {
            Close(slot);
        }
    }

    private void OnOwnerChanged(object? sender, EventArgs e) => Sync();

    private void Place(PaneWindow pane)
    {
        var target = Expected(pane.Slot);
        pane.Position = target.Position;
        // Size in the pane's own DIPs: on a mixed-DPI setup the pane may sit on a different monitor than the owner.
        var ps = pane.DesktopScaling;
        pane.Width = target.Width / ps;
        pane.Height = target.Height / ps;
        pane.MatchOwnerScale(_owner.RenderScaling);
    }
}
