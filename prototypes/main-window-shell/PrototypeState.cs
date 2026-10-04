namespace CmtShellPrototype;

/// <summary>The styling approaches under comparison. Each one layers on top of the previous.</summary>
public enum Variant
{
    /// <summary>Fluent dark + sv_ttk palette + Fluent resource-key overrides only. No styles, no templates.</summary>
    A,

    /// <summary>A + selector <c>Styles</c> that reach into Fluent's templates (<c>/template/</c> part names).</summary>
    B,

    /// <summary>A + our own <c>ControlTheme</c>s with their own templates for the controls whose shape differs.</summary>
    C,
}

/// <summary>Which canned data the Overview shows.</summary>
public enum Scenario
{
    /// <summary>The values in <c>python-overview.png</c>, for a like-for-like side-by-side.</summary>
    Baseline,

    /// <summary>MO2 detected, update banner, limits exceeded, unknown HEDR - exercises every icon and colour.</summary>
    Problems,
}

/// <summary>How the ❌ ✅ 💭 log prefixes are rendered.</summary>
public enum EmojiMode
{
    /// <summary>Default font fallback - Skia picks Segoe UI Emoji, so colour glyphs.</summary>
    Colour,

    /// <summary>Forced to Segoe UI Symbol, so monochrome glyphs tinted by the run's foreground, like Tk 8.6.</summary>
    Mono,
}

/// <summary>PROTOTYPE global switches. Mutable statics are fine here; nothing outlives the process.</summary>
public static class PrototypeState
{
    public static Variant Variant { get; set; } = Variant.C;
    public static Scenario Scenario { get; set; } = Scenario.Baseline;
    public static EmojiMode EmojiMode { get; set; } = EmojiMode.Colour;
    public static bool ForceDwmDarkTitleBar { get; set; }

    /// <summary>Dialog to open once the main window is up (<c>--open</c>), see <c>PrototypeBar.Open</c>.</summary>
    public static string? OpenOnStart { get; set; }

    /// <summary>Text rasterization under test (<c>--textmode</c>); Unspecified leaves Avalonia's platform default.</summary>
    public static Avalonia.Media.TextRenderingMode TextRenderingMode { get; set; } = Avalonia.Media.TextRenderingMode.Unspecified;
}

public static class Variants
{
    public static Variant Parse(string s) => Enum.TryParse<Variant>(s, ignoreCase: true, out var v) ? v : Variant.C;

    public static string Describe(Variant v) => v switch
    {
        Variant.A => "A - palette + resource keys only",
        Variant.B => "B - A + selector Styles into Fluent templates",
        Variant.C => "C - A + own ControlThemes",
        _ => v.ToString(),
    };
}
