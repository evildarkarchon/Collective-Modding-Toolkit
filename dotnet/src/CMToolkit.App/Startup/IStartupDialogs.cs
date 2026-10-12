namespace CMToolkit.App.Startup;

/// <summary>
/// The dialogs startup shows before the main window exists: the <c>tkinter.messagebox</c> and <c>filedialog</c> calls
/// in <c>GameInfo.find_path</c> (GP-2, GP-3). The app's are <see cref="WindowStartupDialogs"/>; parity tests script them.
/// </summary>
public interface IStartupDialogs
{
    /// <summary><c>messagebox.askyesno(title, message)</c>.</summary>
    /// <returns>True for Yes; false for No or a close.</returns>
    Task<bool> AskYesNoAsync(string title, string message);

    /// <summary><c>filedialog.askopenfilename(title=title, filetypes=((fileTypeName, fileTypePattern),))</c>.</summary>
    /// <returns>The picked file's path, or an empty string when the dialog is cancelled, as Tk returns.</returns>
    Task<string> AskOpenFileNameAsync(string title, string fileTypeName, string fileTypePattern);

    /// <summary><c>messagebox.showerror(title, message)</c>; completes when it is dismissed.</summary>
    Task ShowErrorAsync(string title, string message);
}
