using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CMToolkit.App.Views;

/// <summary>Which edge of the main window's client area a docked pane hangs off.</summary>
public enum PaneSlot
{
    /// <summary><c>SidePane</c>: right of the client area, 40 below its top, 5 short of its bottom, 200 wide (SCN-3).</summary>
    Side,

    /// <summary><c>ResultDetailsPane</c>: under the client area, the client's width, 200 tall (SCN-4).</summary>
    Details,
}

/// <summary>
/// A borderless, taskbar-less window owned by the main window: the stand-in for the Scanner panes'
/// <c>Toplevel(root, bd=2, relief=GROOVE).wm_overrideredirect(True)</c>.
/// </summary>
public sealed class PaneWindow : Window
{
    private readonly LayoutTransformControl _host = new();

    /// <param name="slot">Where the pane docks.</param>
    /// <param name="content">The pane's view.</param>
    public PaneWindow(PaneSlot slot, Control content)
    {
        Slot = slot;
        // Never shown (no decorations), but drag/self-test scripts find the panes by it.
        Title = $"Collective Modding Toolkit pane ({slot})";
        WindowDecorations = WindowDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        // Showing a pane must not take activation (and keyboard focus) from the main window, whose Escape still quits.
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SizeToContent = SizeToContent.Manual;
        _host.Child = content;
        Content = _host;
    }

    /// <summary>Where the pane docks.</summary>
    public PaneSlot Slot { get; }

    /// <summary>
    /// Lays the view out in the owner's DIPs. A pane that straddles onto a monitor with another DPI gets that monitor's
    /// scaling, so its fixed physical size holds fewer (or more) DIPs and its content would render too big (or small)
    /// for its box. Scaling by owner/pane undoes that; text is still rasterised at the pane's own density, so it stays
    /// sharp (prototype finding 10, issue #12).
    /// </summary>
    /// <param name="ownerScaling">The owner's render scaling.</param>
    public void MatchOwnerScale(double ownerScaling)
    {
        var k = ownerScaling / RenderScaling;
        _host.LayoutTransform = Math.Abs(k - 1) < 0.001 ? null : new ScaleTransform(k, k);
    }
}

/// <summary>
/// Keeps the Scanner's pane windows glued to the main window (SCN-3..SCN-5), re-placing them on every move, resize and
/// DPI change, which is what the reference's root <c>&lt;Configure&gt;</c> handler (<c>ScannerTab.on_configure</c>) did.
/// </summary>
/// <remarks>
/// <para>The geometry follows the DPI baseline (issue #13): the Tk formulas are evaluated in the owner's DIPs from its
/// client origin (<c>PointToScreen(0,0)</c>, the Tk <c>winfo_rootx/rooty</c>), scaled by the owner's scaling to physical
/// pixels, because <c>Window.Position</c> is physical; the pane's size is then converted back to DIPs with the pane's
/// own <c>DesktopScaling</c>, which differs from the owner's when the pane lands on a monitor with another DPI.</para>
/// <para>Minimising, restoring and stacking are deliberately not handled: Win32 hides owned windows with their owner and
/// re-shows them on restore, and keeps them above it, which replaces <c>CMChecker.on_minimize/on_restore</c> and the
/// <c>tkraise</c> focus handlers (SHELL-11; the prototype's self-test checked both).</para>
/// </remarks>
public sealed class OwnedPaneDocker : IDisposable
{
    private readonly Window _owner;
    private readonly Dictionary<PaneSlot, PaneWindow> _panes = [];
    private bool _syncing;

    /// <param name="owner">The main window the panes dock to and are owned by.</param>
    public OwnedPaneDocker(Window owner)
    {
        _owner = owner;
        _owner.PositionChanged += OnOwnerChanged;
        _owner.Resized += OnOwnerChanged;
        _owner.ScalingChanged += OnOwnerChanged;
    }

    /// <summary>The open panes.</summary>
    public IReadOnlyDictionary<PaneSlot, PaneWindow> Panes => _panes;

    /// <summary>
    /// Opens the pane for <paramref name="slot"/> with <paramref name="content"/>, owned by the main window and placed
    /// (<c>Toplevel(...)</c> + <c>update_geometry</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">That pane is already open.</exception>
    public PaneWindow Show(PaneSlot slot, Control content)
    {
        if (_panes.ContainsKey(slot))
        {
            throw new InvalidOperationException($"The {slot} pane is already open.");
        }

        var pane = new PaneWindow(slot, content);
        _panes[slot] = pane;
        // A pane that crosses onto a monitor with another DPI gets WM_DPICHANGED and Windows resizes it to the
        // suggested rect; re-place it so its physical size matches the owner again.
        pane.ScalingChanged += (_, _) => Sync();
        Place(pane);
        pane.Show(_owner);
        // Placed again once shown: the pane's DesktopScaling is only its monitor's once it has a native window there.
        Place(pane);
        return pane;
    }

    /// <summary>Closes the pane for <paramref name="slot"/> if it is open (<c>pane.destroy()</c>).</summary>
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
    /// Where the pane for <paramref name="slot"/> belongs, in physical screen pixels: the Tk <c>update_geometry</c>
    /// formulas, from the owner's client origin and client size in DIPs, scaled by the owner's render scaling.
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

    /// <summary>Stops following the owner and closes every pane.</summary>
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

    /// <summary>
    /// Moves and sizes <paramref name="pane"/> to its slot beside the owner (<see cref="Expected"/>), converting the
    /// owner's DIPs to the pane's own, and matches its content's scale to the owner's.
    /// </summary>
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
