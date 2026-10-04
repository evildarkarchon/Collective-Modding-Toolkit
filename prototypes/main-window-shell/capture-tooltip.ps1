# PROTOTYPE: hovers the Game Path label and grabs the screen area of the main window (plus room for the popup).
# Tooltips are untitled popups that PrintWindow can't target, so this is a screen grab; it is cropped to the app's
# own rectangle so nothing else on the desktop ends up in the shot.
param([int]$X = 300, [int]$Y = 70, [string]$Out = "dialog-tooltip.png")
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Hov {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    public static System.Collections.Generic.List<IntPtr> Untitled(uint pid) {
        var l = new System.Collections.Generic.List<IntPtr>();
        EnumWindows((h, x) => {
            uint p; GetWindowThreadProcessId(h, out p);
            var s = new System.Text.StringBuilder(256); GetWindowText(h, s, 256);
            if (p == pid && IsWindowVisible(h) && s.Length == 0) l.Add(h);
            return true; }, IntPtr.Zero);
        return l;
    }
    public static IntPtr Find(uint pid, string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            var s = new System.Text.StringBuilder(256); GetWindowText(h, s, 256);
            if (p == pid && s.ToString() == title) { found = h; return false; }
            return true; }, IntPtr.Zero);
        return found;
    }
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
$exe = Join-Path $PSScriptRoot "bin\Debug\net10.0\CmtShellPrototype.exe"
$p = Start-Process $exe -ArgumentList "--variant", "C", "--open", "tooltip" -PassThru
Start-Sleep -Seconds 4
# Not MainWindowHandle: that resolves to the PROTOTYPE controls window.
$h = [Hov]::Find([uint32]$p.Id, "Collective Modding Toolkit v0.6.2-dev")
[void][Hov]::SetForegroundWindow($h)
# Avalonia only shows tooltips in an active window, and SetForegroundWindow is ignored for background callers:
# click an empty spot of the page first to activate it.
$blank = New-Object Hov+POINT; $blank.X = 620; $blank.Y = 140
[void][Hov]::ClientToScreen($h, [ref]$blank)
[void][Hov]::SetCursorPos($blank.X, $blank.Y); Start-Sleep -Milliseconds 200
[Hov]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero); [Hov]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
Start-Sleep -Milliseconds 300
$pt = New-Object Hov+POINT; $pt.X = $X; $pt.Y = $Y
[void][Hov]::ClientToScreen($h, [ref]$pt)
# Two moves: Avalonia only raises PointerEntered on an actual move inside the window.
[void][Hov]::SetCursorPos($pt.X - 5, $pt.Y); Start-Sleep -Milliseconds 300
[void][Hov]::SetCursorPos($pt.X, $pt.Y); Start-Sleep -Milliseconds 1200
# The tooltip is an untitled top-level popup of this process; PrintWindow it directly (no screen grab, so nothing
# else on the desktop can end up in the shot).
$i = 0
foreach ($pop in [Hov]::Untitled([uint32]$p.Id)) {
    $r = New-Object Hov+RECT; [void][Hov]::GetWindowRect($pop, [ref]$r)
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -le 0 -or $hh -le 0) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][Hov]::PrintWindow($pop, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save((Join-Path $PSScriptRoot "shots\popup-$i-$Out")); $bmp.Dispose()
    "popup $i : ${w}x${hh}"
    $i++
}
Stop-Process -Id $p.Id -Force
