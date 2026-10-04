namespace CmtPanesPrototype;

/// <summary>The three ways of hosting the Scanner's side and details panes that are under comparison.</summary>
public enum Variant
{
    /// <summary>
    /// Faithful: two borderless owned <c>Window</c>s glued to the main window's client edges, exactly where the
    /// Python <c>SidePane</c>/<c>ResultDetailsPane</c> Toplevels sit. The main window stays 760x450.
    /// </summary>
    A,

    /// <summary>
    /// In-window, same geometry: the main window itself grows (SizeToContent) by 200 to the right and 200 down
    /// while the Scanner tab needs the panes, and draws them in its own client area at the Tk offsets.
    /// </summary>
    B,

    /// <summary>In-window, fixed 760x450: the panes become a right-hand column and a bottom strip inside the tab.</summary>
    C,
}

/// <summary>PROTOTYPE global switches. Mutable statics are fine here; nothing outlives the process.</summary>
public static class PrototypeState
{
    public static Variant Variant { get; set; } = Variant.A;

    /// <summary>
    /// <c>--pick &lt;text&gt;</c>: once the Scanner tab is up, select the first result whose group title contains
    /// this text, so a details-pane state is reload-stable for screenshots. Null = open the tab, select nothing.
    /// </summary>
    public static string? PickOnStart { get; set; }

    /// <summary><c>--selftest</c>: run <see cref="Views.SelfTest"/> and exit, writing its log to <see cref="SelfTestLog"/>.</summary>
    public static bool SelfTest { get; set; }

    public static string SelfTestLog { get; set; } = Path.Combine(Path.GetTempPath(), "cmt-panes-selftest.log");

    /// <summary><c>--x/--y</c>: initial outer-window position in physical pixels, so the main window can be put on a given monitor.</summary>
    public static Avalonia.PixelPoint? StartPosition { get; set; }

    public static string Describe(Variant v) => v switch
    {
        Variant.A => "A - borderless owned windows (faithful)",
        Variant.B => "B - main window grows, panes drawn inside",
        Variant.C => "C - fixed 760x450, panes inside the tab",
        _ => v.ToString(),
    };
}
