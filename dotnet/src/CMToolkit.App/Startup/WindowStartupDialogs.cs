using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CMToolkit.App.Views;

namespace CMToolkit.App.Startup;

/// <summary>
/// The startup dialogs as the app shows them, with no main window to own them: the app's message boxes, centred on the
/// screen, and the native file picker.
/// </summary>
public sealed class WindowStartupDialogs : IStartupDialogs
{
    /// <inheritdoc/>
    public Task<bool> AskYesNoAsync(string title, string message) => MessageBox.AskYesNoAsync(null, title, message);

    /// <inheritdoc/>
    /// <remarks>
    /// Avalonia's file picker belongs to a top-level window, and there is none yet. Tk's belonged to the withdrawn root,
    /// so a window that is created but never shown stands in for that root: its handle owns the native dialog.
    /// </remarks>
    public async Task<string> AskOpenFileNameAsync(string title, string fileTypeName, string fileTypePattern)
    {
        var owner = new Window { ShowInTaskbar = false };
        try
        {
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = [fileTypePattern] }],
            });

            // Tk returns an empty string for Cancel, and for a pick with no local path there is nothing to return either.
            return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
        }
        finally
        {
            owner.Close();
        }
    }

    /// <inheritdoc/>
    public Task ShowErrorAsync(string title, string message) => MessageBox.ShowErrorAsync(null, title, message);
}
