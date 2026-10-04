using System.Reflection;
using Avalonia.Controls;

namespace CMToolkit.Tests.Support;

/// <summary>Simulates the user closing a window from its title bar (or Alt+F4), which headless input can't do.</summary>
public static class UserClose
{
    /// <summary>
    /// <c>Window.HandleClosing</c>, the handler Avalonia hands the platform window as its close callback. The callback
    /// itself (<c>IWindowImpl.Closing</c>) is private API in Avalonia 12, so tests reach the handler by reflection; if an
    /// Avalonia upgrade renames it, this fails loudly rather than silently testing nothing.
    /// </summary>
    private static readonly MethodInfo HandleClosing =
        typeof(Window).GetMethod("HandleClosing", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingMethodException(nameof(Window), "HandleClosing");

    /// <summary>
    /// Asks the window to close the way the platform does when the user clicks its close button, so
    /// <see cref="Window.Closing"/> sees a non-programmatic <see cref="WindowCloseReason.WindowClosing"/>.
    /// </summary>
    /// <returns>Whether the window closed (nothing cancelled the close).</returns>
    public static bool Request(Window window)
    {
        var cancelled = (bool)HandleClosing.Invoke(window, [WindowCloseReason.WindowClosing])!;
        if (!cancelled)
        {
            // A real platform window destroys itself when the callback doesn't cancel; the headless one leaves it to us.
            window.PlatformImpl!.Dispose();
        }

        return !cancelled;
    }
}
