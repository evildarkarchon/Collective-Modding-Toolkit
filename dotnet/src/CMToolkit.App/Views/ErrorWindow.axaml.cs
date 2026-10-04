using Avalonia.Controls;

namespace CMToolkit.App.Views;

/// <summary>
/// The Error Window, "An Error Occurred". <see cref="Runtime.ErrorReporter"/> owns its lifetime: it opens one on the
/// first error, appends later errors to it, and opens a new one after the user closes it.
/// </summary>
public partial class ErrorWindow : Window
{
    /// <summary>An empty Error Window; <see cref="Append"/> adds the reports.</summary>
    public ErrorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Everything reported to this window so far.</summary>
    public string Text => Output.Text ?? "";

    /// <summary>
    /// Adds one report (a header line and the exception) below the earlier ones, separated by a blank line, and moves
    /// the caret to it so the newest error is in view.
    /// </summary>
    public void Append(string report)
    {
        Output.Text = Text.Length == 0 ? report : $"{Text}\n\n{report}";
        Output.CaretIndex = Output.Text.Length;
    }
}
