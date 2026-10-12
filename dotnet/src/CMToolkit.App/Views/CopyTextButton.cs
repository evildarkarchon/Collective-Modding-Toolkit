using Avalonia.Controls;
using Avalonia.Input.Platform;
using CMToolkit.App.Runtime;

namespace CMToolkit.App.Views;

/// <summary>
/// <c>utils.copy_text_button</c>: a button that copies a text to the clipboard, then reads <c>Copied!</c>, disabled,
/// for 3 s before going back to its own text (THR-3). Used by the About tab's copy buttons and the Scanner details
/// pane's <c>Copy Details</c>.
/// </summary>
public static class CopyTextButton
{
    /// <summary>The text a copy button shows while it is disabled.</summary>
    public const string CopiedText = "Copied!";

    /// <summary>How long a copy button stays on <see cref="CopiedText"/>: the reference's <c>after(3000)</c>.</summary>
    public static readonly TimeSpan RevertAfter = TimeSpan.FromMilliseconds(3000);

    /// <summary>
    /// Makes <paramref name="button"/> copy <paramref name="text"/> when clicked. A failure is reported to the Error
    /// Window as an exception on the UI thread, like any other callback's.
    /// </summary>
    /// <param name="button">The button; its content is its idle text.</param>
    /// <param name="text">What it copies.</param>
    /// <param name="runtime">The app runtime: its clock times the revert, its error reporter takes failures.</param>
    public static void Attach(Button button, string text, AppRuntime runtime)
    {
        button.Click += async (_, _) =>
        {
            try
            {
                await CopyAsync(button, text, runtime.Time);
            }
            catch (Exception ex)
            {
                runtime.Errors.Report(ErrorReporter.UiThreadHeader, ex);
            }
        };
    }

    /// <summary>
    /// One click: the clipboard is replaced (<c>clipboard_clear</c> + <c>clipboard_append</c>), the button shows
    /// <c>Copied!</c> disabled, and after <see cref="RevertAfter"/> it gets back the content it had when clicked.
    /// </summary>
    /// <exception cref="InvalidOperationException">The button is not in a window, so there is no clipboard.</exception>
    public static async Task CopyAsync(Button button, string text, TimeProvider time)
    {
        var clipboard = TopLevel.GetTopLevel(button)?.Clipboard
            ?? throw new InvalidOperationException("The copy button is not in a window with a clipboard.");
        await clipboard.SetTextAsync(text);

        var original = button.Content;
        button.Content = CopiedText;
        button.IsEnabled = false;
        // The continuation comes back on the UI thread through its synchronization context, as Tk's after() did.
        await Task.Delay(RevertAfter, time);
        button.Content = original;
        button.IsEnabled = true;
    }
}
