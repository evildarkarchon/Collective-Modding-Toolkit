# PROTOTYPE helper: clicks at a client-area coordinate (in that window's own pixel space) of the first window whose title matches.
param([string]$Pattern, [int]$X, [int]$Y)
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Clk {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static IntPtr Find(string pat) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            if (!IsWindowVisible(h)) return true;
            var s = new StringBuilder(512); GetWindowText(h, s, 512);
            if (System.Text.RegularExpressions.Regex.IsMatch(s.ToString(), pat)) { found = h; return false; }
            return true; }, IntPtr.Zero);
        return found;
    }
}
"@
$h = [Clk]::Find($Pattern)
if ($h -eq [IntPtr]::Zero) { throw "no window matching $Pattern" }
$p = New-Object Clk+POINT; $p.X = $X; $p.Y = $Y
[void][Clk]::ClientToScreen($h, [ref]$p)
[void][Clk]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 200
[void][Clk]::SetCursorPos($p.X, $p.Y)
Start-Sleep -Milliseconds 100
[Clk]::mouse_event(0x2, 0, 0, 0, [IntPtr]::Zero)
[Clk]::mouse_event(0x4, 0, 0, 0, [IntPtr]::Zero)
"clicked $($p.X),$($p.Y)"
