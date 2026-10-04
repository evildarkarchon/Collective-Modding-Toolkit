using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;

namespace CmtPanesPrototype;

/// <summary>PROTOTYPE Win32 probes for the readout: the real on-screen rect and visibility of a window, as the OS sees them.</summary>
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hWnd);

    /// <summary>GetWindowRect in physical pixels (a borderless pane has no invisible resize border, so this is its visible rect).</summary>
    public static PixelRect WindowRect(TopLevel w)
    {
        var h = w.TryGetPlatformHandle()?.Handle ?? 0;
        return GetWindowRect(h, out var r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;
    }

    /// <summary>IsWindowVisible: false while Windows has hidden an owned window because its owner is minimised.</summary>
    public static bool IsVisible(TopLevel w) => IsWindowVisible(w.TryGetPlatformHandle()?.Handle ?? 0);

    public static uint Dpi(TopLevel w) => GetDpiForWindow(w.TryGetPlatformHandle()?.Handle ?? 0);
}
