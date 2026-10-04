using System.Text;
using Avalonia;
using Avalonia.Controls;
using CmtPanesPrototype.Docking;

namespace CmtPanesPrototype.Views;

/// <summary>
/// Surfaces the docking state: for each pane, where the Tk formulas say it should be (physical px) against where the
/// OS actually has it, plus DPI and visibility. "OK" means within 1 px; anything else is "DRIFT".
/// </summary>
public static class Readout
{
    /// <summary>Formats the current state of <paramref name="main"/> and its panes as a few readable lines.</summary>
    public static string Describe(MainWindow main)
    {
        var sb = new StringBuilder();
        var origin = main.PointToScreen(default);
        sb.Append($"main: state={main.WindowState} clientOrigin=({origin.X},{origin.Y}) client={main.ClientSize.Width}x{main.ClientSize.Height} DIP ")
          .Append($"scale={main.RenderScaling:0.##} dpi={Native.Dpi(main)} outer={Fmt(Native.WindowRect(main))}");

        if (main.Docker is not { } docker)
        {
            sb.Append($"\n(variant {main.Variant}: panes are inside the main window; nothing to drift)");
            return sb.ToString();
        }

        sb.Append($"  syncs={docker.SyncCount}");
        foreach (var slot in new[] { PaneSlot.Side, PaneSlot.Details })
        {
            if (!docker.Panes.TryGetValue(slot, out var pane))
            {
                sb.Append($"\n{slot,-7}: (not open)");
                continue;
            }

            var expected = docker.Expected(slot);
            var actual = Native.WindowRect(pane);
            sb.Append($"\n{slot,-7}: expected={Fmt(expected)} actual={Fmt(actual)} ")
              .Append($"dpi={Native.Dpi(pane)} visible={Native.IsVisible(pane)} -> {(Close(expected, actual) ? "OK" : "DRIFT")}");
        }

        return sb.ToString();
    }

    private static bool Close(PixelRect a, PixelRect b)
        => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1 && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

    private static string Fmt(PixelRect r) => $"{r.Width}x{r.Height}+{r.X}+{r.Y}";
}
