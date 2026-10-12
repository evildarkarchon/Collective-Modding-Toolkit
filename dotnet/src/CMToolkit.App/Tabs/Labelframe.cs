using Avalonia;
using Avalonia.Controls;

namespace CMToolkit.App.Tabs;

/// <summary>Helpers for <see cref="GroupBox"/>es standing in for <c>ttk.Labelframe</c>s.</summary>
public static class Labelframe
{
    /// <summary>
    /// The <c>GroupBox.Padding</c> that reproduces <c>ttk.Labelframe(padding=p)</c>: content <paramref name="padding"/>
    /// px inside the frame's left, right and bottom edges and below its label. The labelframe control themes already
    /// keep a 4 px gutter on the left, right and bottom (sv_ttk's default labelframe padding), so only the rest is added.
    /// </summary>
    public static Thickness Padding(int padding) => new(padding - 4, padding, padding - 4, padding - 4);

    /// <summary>
    /// A labelframe's title, at <c>FONT_SMALL</c> (<c>style.configure("TLabelframe.Label", font=FONT_SMALL)</c>).
    /// </summary>
    /// <remarks>
    /// A string header would render at the app-wide normal size: its TextBlock's logical parent is the GroupBox, not
    /// the template's header presenter, so no <c>/template/</c> style can single it out from the frame's content.
    /// </remarks>
    public static TextBlock Title(string text)
    {
        var title = new TextBlock { Text = text };
        title.Classes.Add("small");
        return title;
    }
}
