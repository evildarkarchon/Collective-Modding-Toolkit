using System.ComponentModel;
using System.Diagnostics;

namespace CMToolkit.App.Runtime;

/// <summary>
/// Opens links in the user's browser (<c>webbrowser.open</c>, NET-4): the Tools and About tabs, and later the update
/// banner and the Overview's solution links. Tests swap in a recorder through <see cref="AppRuntime.Browser"/>.
/// </summary>
public interface IBrowser
{
    /// <summary>Opens <paramref name="url"/>, on the calling (UI) thread as the reference did.</summary>
    /// <returns>Whether it opened; a failure is not an error, as <c>webbrowser.open</c> only returns False.</returns>
    bool Open(string url);
}

/// <summary>
/// The real browser: the URL handed to the shell (<c>ShellExecute</c> "open"), which is what Python's
/// <c>webbrowser.WindowsDefault</c> does through <c>os.startfile</c>.
/// </summary>
public sealed class ShellBrowser : IBrowser
{
    private ShellBrowser()
    {
    }

    /// <summary>The single instance; it has no state.</summary>
    public static ShellBrowser Instance { get; } = new();

    /// <inheritdoc/>
    /// <remarks>
    /// A shell failure (no handler for the scheme) returns false and is otherwise ignored: <c>WindowsDefault.open</c>
    /// catches the <c>OSError</c>, and none of the reference's callers look at the result. Python would then try the
    /// browsers it found on PATH or Edge's install folder; that fallback is not reproduced (dotnet/README.md, Known
    /// residuals).
    /// </remarks>
    public bool Open(string url)
    {
        try
        {
            // UseShellExecute hands the URL to the shell's default handler instead of treating it as an exe path.
            using var process = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
