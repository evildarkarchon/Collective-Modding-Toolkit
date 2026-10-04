using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace CmtPanesPrototype.Views;

/// <summary>
/// <c>--selftest</c>: walks the main window through the situations the ticket names (move, other monitor, straddling
/// monitors, minimise/restore, tab switch, rescan) and logs <see cref="Readout"/> after each, then exits. Programmatic
/// moves only; the interactive title-bar drag is checked separately by drag.ps1 with real mouse input.
/// </summary>
public static class SelfTest
{
    /// <summary>Runs the script on the UI thread. Each step waits long enough for WM_MOVE/WM_DPICHANGED to settle.</summary>
    public static async Task RunAsync(MainWindow main)
    {
        await using var log = new StreamWriter(PrototypeState.SelfTestLog, append: false);
        async Task Step(string name, Action? act = null, int waitMs = 400)
        {
            act?.Invoke();
            await Task.Delay(waitMs);
            await log.WriteLineAsync($"## {name}\n{Readout.Describe(main)}\n");
        }

        await log.WriteLineAsync($"variant {main.Variant}: {PrototypeState.Describe(main.Variant)}\n");
        await Step("opened, Scanner tab, side pane only", waitMs: 800);
        await Step("row selected -> details pane", () => main.Pick("Race Subgraph"));
        await Step("move +137/+61", () => main.Position += new PixelPoint(137, 61));

        var screens = main.Screens.All.ToList();
        if (screens.Count > 1)
        {
            var here = main.Screens.ScreenFromWindow(main) ?? screens[0];
            var other = screens.First(s => !ReferenceEquals(s, here) && s.Bounds != here.Bounds);
            var offset = main.Position - here.Bounds.Position;
            await Step($"moved to other monitor ({other.Bounds}, scale {other.Scaling})", () => main.Position = other.Bounds.Position + offset, 800);

            await Step("back to the original monitor", () => main.Position = here.Bounds.Position + offset, 800);

            // Measured back on the original monitor: the outer/client geometry depends on the monitor's DPI. Put the
            // client's right edge 60 px short of the monitor edge, so 140 of the side pane's 200 px (and its centre,
            // which decides its DPI) are on the neighbour while the owner stays here.
            var outer = Native.WindowRect(main);
            var clientLeft = main.PointToScreen(default).X - outer.X;
            var clientWidth = (int)Math.Round(main.ClientSize.Width * main.RenderScaling);
            await Step("straddling: owner here, side pane mostly on the neighbouring monitor",
                () => main.Position = new PixelPoint(here.Bounds.Right - 60 - clientWidth - clientLeft, here.Bounds.Y + 200), 800);
            await Step("back again", () => main.Position = here.Bounds.Position + offset, 800);
        }

        await Step("minimised (panes should be hidden by the OS)", () => main.WindowState = WindowState.Minimized, 800);
        await Step("restored (panes should be back, in place)", () => main.WindowState = WindowState.Normal, 800);
        await Step("switched to Overview (both panes destroyed)", () => main.SelectTab(0));
        await Step("back to Scanner (side pane recreated, no details)", () => main.SelectTab(2));
        await Step("row selected again", () => main.Pick("Invalid Archive"));
        await Step("Scan Game (details destroyed; scanning)", main.StartScan, 300);
        await Step("scan finished", waitMs: 2000);

        await log.FlushAsync();
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
