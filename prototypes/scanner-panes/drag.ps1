# PROTOTYPE: drags a main window by its title bar with real mouse input (the OS modal move loop, which programmatic
# moves don't exercise) and samples the main/side/details window rects while the button is held. Reports, per
# sample, how far each pane is from where the Tk formulas put it relative to the main window's client origin.
# Works for both apps: windows are found by process id and outer size (main 776x489, side 200x405, details 760x200).
# Usage: pwsh ./drag.ps1 -ProcessId <pid> [-Steps 40] [-Dx 12] [-Dy 4] [-Shot <png path>]
param(
    [Parameter(Mandatory)] [int]$ProcessId,
    [int]$Steps = 40,
    [int]$Dx = 12,
    [int]$Dy = 4,
    [string]$Shot = ""
)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class D {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int x, int y, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    public static List<IntPtr> ForPid(uint pid) {
        var l = new List<IntPtr>();
        EnumWindows((h, p) => { uint q; GetWindowThreadProcessId(h, out q); if (q == pid && IsWindowVisible(h)) l.Add(h); return true; }, IntPtr.Zero);
        return l;
    }
}
"@
function Rect($h) { $r = New-Object D+RECT; [void][D]::GetWindowRect($h, [ref]$r); $r }
function Find($w, $hh) { [D]::ForPid($ProcessId) | Where-Object { $r = Rect $_; ($r.R - $r.L) -eq $w -and ($r.B - $r.T) -eq $hh } | Select-Object -First 1 }

$main = Find 776 489; $side = Find 200 405; $details = Find 760 200
if (-not $main) { throw "main window (776x489) not found for pid $ProcessId" }
[void][D]::SetForegroundWindow($main)
Start-Sleep -Milliseconds 300

function Sample($label) {
    $o = New-Object D+POINT; [void][D]::ClientToScreen($main, [ref]$o)
    $line = "{0,-6} client=({1},{2})" -f $label, $o.X, $o.Y
    $worst = 0
    if ($side) { $s = Rect $side; $ex = $o.X + 760; $ey = $o.Y + 40; $d = [Math]::Max([Math]::Abs($s.L - $ex), [Math]::Abs($s.T - $ey)); $worst = [Math]::Max($worst, $d); $line += " side off by $d" }
    if ($details) { $s = Rect $details; $ex = $o.X; $ey = $o.Y + 450; $d = [Math]::Max([Math]::Abs($s.L - $ex), [Math]::Abs($s.T - $ey)); $worst = [Math]::Max($worst, $d); $line += " details off by $d" }
    Write-Host $line
    return $worst
}

$r = Rect $main
$x = $r.L + 300; $y = $r.T + 15   # a blank spot on the title bar
[void][D]::SetCursorPos($x, $y)
Start-Sleep -Milliseconds 100
[D]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)   # LEFTDOWN
Start-Sleep -Milliseconds 150
$max = 0; $off = 0
for ($i = 1; $i -le $Steps; $i++) {
    [void][D]::SetCursorPos($x + $i * $Dx, $y + $i * $Dy)
    Start-Sleep -Milliseconds 16
    $w = Sample "drag$i"
    if ($w -gt 0) { $off++ }
    $max = [Math]::Max($max, $w)
    if ($Shot -and $i -eq [int]($Steps / 2)) {
        $o = New-Object D+POINT; [void][D]::ClientToScreen($main, [ref]$o)
        $bmp = New-Object System.Drawing.Bitmap 1000, 720
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($o.X - 20, $o.Y - 50, 0, 0, $bmp.Size); $g.Dispose()
        $bmp.Save($Shot); $bmp.Dispose()
    }
}
[D]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)   # LEFTUP
Start-Sleep -Milliseconds 400
$settled = Sample "after"
"SUMMARY: $off/$Steps mid-drag samples had a pane off its anchor; worst $max px; after release off by $settled px"
