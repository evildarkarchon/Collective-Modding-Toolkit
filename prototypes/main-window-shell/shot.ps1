# PROTOTYPE helper: captures top-level windows whose title matches a pattern, via PrintWindow.
# Usage: pwsh shot.ps1 -Pattern "Collective Modding" -OutDir <dir> [-List]
param(
    [string]$Pattern = "",
    [string]$OutDir = ".",
    [switch]$List
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class Win {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr c);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    public static List<IntPtr> All() {
        var l = new List<IntPtr>();
        EnumWindows((h, p) => { if (IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero);
        return l;
    }
    public static string Title(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
}
"@

foreach ($h in [Win]::All()) {
    $t = [Win]::Title($h)
    if (-not $t) { continue }
    if ($Pattern -and ($t -notmatch $Pattern)) { continue }
    $r = New-Object Win+RECT; [void][Win]::GetWindowRect($h, [ref]$r)
    $c = New-Object Win+RECT; [void][Win]::GetClientRect($h, [ref]$c)
    $pid2 = 0; [void][Win]::GetWindowThreadProcessId($h, [ref]$pid2)
    $aw = [Win]::GetAwarenessFromDpiAwarenessContext([Win]::GetWindowDpiAwarenessContext($h))
    "{0} | pid {1} | outer {2}x{3} | client {4}x{5} | dpi {6} | awareness {7}" -f $t, $pid2, ($r.R - $r.L), ($r.B - $r.T), $c.R, $c.B, [Win]::GetDpiForWindow($h), $aw
    if ($List) { continue }
    $w = $r.R - $r.L; $hh = $r.B - $r.T
    if ($w -le 0 -or $hh -le 0) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # PW_RENDERFULLCONTENT = 2, needed for DirectComposition/GPU-rendered windows like Avalonia's.
    [void][Win]::PrintWindow($h, $hdc, 2)
    $g.ReleaseHdc($hdc); $g.Dispose()
    $safe = ($t -replace '[^\w\-\. ]', '_')
    $path = Join-Path $OutDir "$safe.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    "  -> $path"
}
