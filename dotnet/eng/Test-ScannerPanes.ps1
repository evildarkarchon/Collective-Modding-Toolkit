<#
.SYNOPSIS
    Walks the published exe's Scanner panes through moves, monitors, minimise/restore, tab switches and a rescan,
    checking each pane's on-screen rect against the reference's formulas after every step.

.DESCRIPTION
    The real-desktop verification of the Scanner's docked panes (SCN-3..SCN-5, SHELL-11), lifted from the panes
    prototype's --selftest and drag.ps1 (issue #12, branch prototype/scanner-panes). Headless tests prove the placement
    formulas; only a real desktop shows that Win32 really moves, hides and re-shows the owned windows with their owner.

    The exe runs on a Parity Scenario's machine (default ../parity/scenarios/scanner/overview-issues), materialized as
    Test-ShellStarts.ps1 does. Each step is driven with posted messages or SetWindowPos/ShowWindow, which don't touch
    the real pointer, then the side and details panes (found by their titles) are compared with where the reference's
    update_geometry puts them, from the main window's client origin, scaled by its DPI:

        side     200 x (450 - 45) at client (760, 40)
        details  760 x 200        at client (0, 450)

    Steps: Scanner tab opened (side pane only); an Overview Issues scan and an item selected (details pane); a move of
    +137/+61; with a second monitor, a move there, back, and straddling the edge with the side pane over it; minimise
    (both panes hidden by the OS) and restore (back, in place); the Overview tab (both closed); the Scanner again (side
    only); an item selected and Scan Game (details closed).

    With -Drag, it also drags the main window by its title bar with REAL mouse input (the OS modal move loop, which
    programmatic moves don't exercise), sampling the panes mid-drag. That moves the user's pointer and presses its
    button: run it only on a desktop nobody is using.

    Run it at 100 % and again with the main window's monitor at 150 % (and, with two monitors at different scales, the
    straddling step covers mixed DPI). It exits non-zero if any step leaves a pane off its anchor or wrongly shown.

.PARAMETER Exe
    The published cm-toolkit.exe.

.PARAMETER Scenario
    The Parity Scenario folder to run on.

.PARAMETER Drag
    Also drag the main window with real mouse input.

.PARAMETER Log
    If set, a file the step-by-step readout is also written to.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Exe,
    [string] $Scenario = (Join-Path $PSScriptRoot '..\..\parity\scenarios\scanner\overview-issues'),
    [switch] $Drag,
    [string] $Log
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmtPanes {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    public delegate bool MonitorProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, int x, int y, uint data, UIntPtr extra);

    public static List<IntPtr> VisibleWindowsOf(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((h, _) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == pid && IsWindowVisible(h)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Every window of the process, visible or not: a pane hidden with its minimised owner still exists.
    public static List<IntPtr> WindowsOf(uint pid) {
        var found = new List<IntPtr>();
        EnumWindows((h, _) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == pid) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static List<RECT> Monitors() {
        var found = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr dc, ref RECT r, IntPtr d) => { found.Add(r); return true; }, IntPtr.Zero);
        return found;
    }

    public static string TitleOf(IntPtr hwnd) {
        var text = new StringBuilder(512);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    // A left click at a client point of the deepest window there, posted: the real pointer doesn't move.
    public static void Click(IntPtr hwnd, int x, int y) {
        var lParam = (IntPtr)((y << 16) | (x & 0xFFFF));
        PostMessage(hwnd, 0x0201, (IntPtr)1, lParam);
        PostMessage(hwnd, 0x0202, IntPtr.Zero, lParam);
    }
}
'@

# Physical coordinates for every window whatever its monitor (PER_MONITOR_AWARE_V2); a no-op if pwsh already is.
[void][CmtPanes]::SetProcessDpiAwarenessContext([IntPtr]-4)

$title = 'Collective Modding Toolkit v0.6.2-dev'
$sideTitle = 'Collective Modding Toolkit pane (Side)'
$detailsTitle = 'Collective Modding Toolkit pane (Details)'
# Client points in the 760x450 main window and the 200x405 side pane (DIPs; scaled by the DPI before posting).
$scannerTab = @(220, 25)
$overviewTab = @(53, 25)
$firstItem = @(100, 112)
$dataBoxes = @(@(26, 70), @(26, 98), @(26, 126), @(26, 154), @(26, 182), @(26, 210))
$scanButton = @(100, 375)

$lines = New-Object System.Collections.Generic.List[string]
function Say([string] $text) {
    Write-Host $text
    $lines.Add($text)
}

$exePath = (Resolve-Path -LiteralPath $Exe).ProviderPath
$scenarioDir = (Resolve-Path -LiteralPath $Scenario).ProviderPath
$manifestPath = Join-Path $scenarioDir 'scenario.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("cmt-panes-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir | Out-Null
Copy-Item -Recurse -Path (Join-Path $scenarioDir 'tree\*') -Destination $workDir
foreach ($dir in @($manifest.emptyDirs)) {
    if ($dir) { New-Item -ItemType Directory -Force -Path (Join-Path $workDir $dir) | Out-Null }
}
$startDir = $workDir
if ($manifest.host.cwd) {
    $startDir = $workDir + $manifest.host.cwd.Substring(6)
}

$env:CMT_PARITY_SCENARIO = $manifestPath
$env:CMT_PARITY_ROOT = $workDir
try {
    $process = Start-Process -FilePath $exePath -WorkingDirectory $startDir -PassThru
}
finally {
    Remove-Item Env:CMT_PARITY_SCENARIO, Env:CMT_PARITY_ROOT -ErrorAction SilentlyContinue
}

$failures = 0
try {
    $pid32 = [uint32]$process.Id
    $main = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline -and $main -eq [IntPtr]::Zero) {
        $main = [CmtPanes]::VisibleWindowsOf($pid32) | Where-Object { [CmtPanes]::TitleOf($_) -eq $title } | Select-Object -First 1
        if (-not $main) { $main = [IntPtr]::Zero; Start-Sleep -Milliseconds 250 }
    }
    if ($main -eq [IntPtr]::Zero) { throw "No main window titled '$title'." }
    Start-Sleep -Seconds 2

    function Scale { [CmtPanes]::GetDpiForWindow($main) / 96.0 }
    function Pane([string] $paneTitle) {
        [CmtPanes]::WindowsOf($pid32) | Where-Object { [CmtPanes]::TitleOf($_) -eq $paneTitle } | Select-Object -First 1
    }
    function ClickClient([IntPtr] $hwnd, $point) {
        $k = [CmtPanes]::GetDpiForWindow($hwnd) / 96.0
        [CmtPanes]::Click($hwnd, [int][math]::Round($point[0] * $k), [int][math]::Round($point[1] * $k))
    }

    # One pane against its anchor: "ok", or what is off. $expectShown: $true, $false (hidden) or $null (no window).
    function Check([string] $name, [string] $paneTitle, $expectShown, $dx, $dy, $dw, $dh) {
        $hwnd = Pane $paneTitle
        if ($null -eq $expectShown) {
            if ($hwnd) { return "$name exists but shouldn't" }
            return "$name none"
        }
        if (-not $hwnd) { return "$name MISSING" }
        $visible = [CmtPanes]::IsWindowVisible($hwnd)
        if ($visible -ne $expectShown) { return "$name visible=$visible, expected $expectShown" }
        if (-not $expectShown) { return "$name hidden" }
        $o = New-Object CmtPanes+POINT
        [void][CmtPanes]::ClientToScreen($main, [ref]$o)
        $k = Scale
        $r = New-Object CmtPanes+RECT
        [void][CmtPanes]::GetWindowRect($hwnd, [ref]$r)
        $ex = $o.X + [math]::Round($dx * $k); $ey = $o.Y + [math]::Round($dy * $k)
        $ew = [math]::Round($dw * $k); $eh = [math]::Round($dh * $k)
        $off = @([math]::Abs($r.Left - $ex), [math]::Abs($r.Top - $ey), [math]::Abs($r.Right - $r.Left - $ew), [math]::Abs($r.Bottom - $r.Top - $eh)) |
            Measure-Object -Maximum | ForEach-Object { [int]$_.Maximum }
        $dpi = [CmtPanes]::GetDpiForWindow($hwnd)
        if ($off -gt 0) { return "$name DRIFT ${off}px (at $($r.Left),$($r.Top) $($r.Right - $r.Left)x$($r.Bottom - $r.Top), expected $ex,$ey ${ew}x$eh, pane dpi $dpi)" }
        return "$name ok ($($r.Right - $r.Left)x$($r.Bottom - $r.Top) at pane dpi $dpi)"
    }

    function Step([string] $name, [scriptblock] $act, $side, $details, [int] $waitMs = 600) {
        if ($act) { & $act }
        Start-Sleep -Milliseconds $waitMs
        $s = Check 'side' $sideTitle $side 760 40 200 405
        $d = Check 'details' $detailsTitle $details 0 450 760 200
        $bad = ($s + $d) -match 'DRIFT|MISSING|exists|expected'
        if ($bad) { $script:failures++ }
        Say ("{0,-4} {1}: {2}; {3} (main scale {4})" -f $(if ($bad) { 'FAIL' } else { 'OK' }), $name, $s, $d, (Scale))
    }

    function MoveMain([int] $x, [int] $y) {
        # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
        [void][CmtPanes]::SetWindowPos($main, [IntPtr]::Zero, $x, $y, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010)
    }
    function MainRect {
        $r = New-Object CmtPanes+RECT
        [void][CmtPanes]::GetWindowRect($main, [ref]$r)
        $r
    }

    Step 'Scanner tab opened' { ClickClient $main $scannerTab } $true $null 1500
    Step 'data boxes unticked, Overview Issues scanned' {
        $side = Pane $sideTitle
        foreach ($point in $dataBoxes) { ClickClient $side $point; Start-Sleep -Milliseconds 150 }
        ClickClient $side $scanButton
    } $true $null 3000
    Step 'item selected' { ClickClient $main $firstItem } $true $true 1000
    Step 'moved +137/+61' { $r = MainRect; MoveMain ($r.Left + 137) ($r.Top + 61) } $true $true

    $monitors = [CmtPanes]::Monitors()
    if ($monitors.Count -gt 1) {
        $start = MainRect
        $here = $monitors | Where-Object { $start.Left -ge $_.Left -and $start.Left -lt $_.Right -and $start.Top -ge $_.Top -and $start.Top -lt $_.Bottom } | Select-Object -First 1
        if (-not $here) { $here = $monitors[0] }
        $other = $monitors | Where-Object { $_.Left -ne $here.Left -or $_.Top -ne $here.Top } | Select-Object -First 1
        Step "moved to the other monitor ($($other.Left),$($other.Top))" { MoveMain ($other.Left + 100) ($other.Top + 100) } $true $true 1000
        Step 'back on the first monitor' { MoveMain $start.Left $start.Top } $true $true 1000
        # The client's right edge 60 px short of the monitor's right edge: most of the side pane is on the neighbour.
        Step 'straddling: the side pane mostly on the neighbouring monitor' {
            $o = New-Object CmtPanes+POINT
            [void][CmtPanes]::ClientToScreen($main, [ref]$o)
            $r = MainRect
            $clientWidth = [math]::Round(760 * (Scale))
            MoveMain ($here.Right - 60 - $clientWidth - ($o.X - $r.Left)) ($here.Top + 200)
        } $true $true 1000
        Step 'back again' { MoveMain $start.Left $start.Top } $true $true 1000
    }
    else {
        Say 'SKIP one monitor: the other-monitor and straddling steps need two.'
    }

    if ($Drag) {
        # REAL mouse input: press on a blank spot of the title bar and drag in 40 steps, sampling the panes.
        [void][CmtPanes]::SetForegroundWindow($main)
        Start-Sleep -Milliseconds 300
        $r = MainRect
        $x = $r.Left + 300; $y = $r.Top + 15
        [void][CmtPanes]::SetCursorPos($x, $y)
        Start-Sleep -Milliseconds 100
        [CmtPanes]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        $off = 0
        for ($i = 1; $i -le 40; $i++) {
            [void][CmtPanes]::SetCursorPos($x + $i * 12, $y + $i * 4)
            Start-Sleep -Milliseconds 16
            $s = Check 'side' $sideTitle $true 760 40 200 405
            $d = Check 'details' $detailsTitle $true 0 450 760 200
            if (($s + $d) -match 'DRIFT|MISSING') { $off++ }
        }
        [CmtPanes]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
        if ($off -gt 0) { $failures++ }
        Say ("{0,-4} real title-bar drag: {1}/40 mid-drag samples had a pane off its anchor" -f $(if ($off -gt 0) { 'FAIL' } else { 'OK' }), $off)
        Step 'after the drag' $null $true $true
    }

    # SW_MINIMIZE / SW_RESTORE: Win32 hides owned windows with their owner and re-shows them (SHELL-11).
    Step 'minimised (panes hidden by the OS)' { [void][CmtPanes]::ShowWindow($main, 6) } $false $false 1000
    Step 'restored (panes back, in place)' { [void][CmtPanes]::ShowWindow($main, 9) } $true $true 1000
    Step 'Overview tab (both panes closed)' { ClickClient $main $overviewTab } $null $null 1000
    Step 'Scanner tab again (side pane only)' { ClickClient $main $scannerTab } $true $null 1000
    Step 'item selected again' { ClickClient $main $firstItem } $true $true 1000
    Step 'Scan Game (details pane closed)' { ClickClient (Pane $sideTitle) $scanButton } $true $null 2000
}
finally {
    if (-not $process.HasExited) {
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { $process.Kill() }
    }
    Remove-Item -Recurse -Force -LiteralPath $workDir -ErrorAction SilentlyContinue
    if ($Log) { $lines | Set-Content -LiteralPath $Log -Encoding utf8NoBOM }
}

if ($failures -gt 0) {
    throw "$failures step(s) left a pane off its anchor or wrongly shown."
}
Say 'OK: the Scanner panes stayed docked.'
