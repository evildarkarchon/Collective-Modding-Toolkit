using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace CmtShellPrototype;

/// <summary>PROTOTYPE Win32 calls for the two "is the native route better?" comparisons.</summary>
internal static partial class Native
{
    private const uint MbOk = 0x0;
    private const uint MbIconWarning = 0x30;
    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(nint hWnd, string text, string caption, uint type);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// What tkinter.messagebox.showwarning does on Windows: a native, owner-modal MessageBoxW. Blocks the UI thread
    /// inside the system modal loop, exactly like Tk (Avalonia windows keep painting because the loop pumps messages).
    /// </summary>
    public static void WarningBox(TopLevel owner, string caption, string text)
        => MessageBoxW(owner.TryGetPlatformHandle()?.Handle ?? 0, text, caption, MbOk | MbIconWarning);

    /// <summary>utils.set_titlebar_style: DWM attributes 19 and 20 = immersive dark mode on.</summary>
    public static void ForceDarkTitleBar(TopLevel window)
    {
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        var on = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeOld, ref on, sizeof(int));
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }
}
