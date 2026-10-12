<#
.SYNOPSIS
    Starts the published exe, waits for the shell window, checks it, optionally screenshots it, and closes it.

.DESCRIPTION
    A smoke test for the NativeAOT build. Some AOT failures only show up at runtime: a XAML include loaded by URI
    throws "No precompiled XAML found" before any window appears, for example. Headless tests run on the JIT, so they
    can't catch those.

    It checks that a visible top-level window owned by the process appears with the expected title, and that its
    client area is 760x450 at 100% scaling (scaled by the window's DPI otherwise). The title is built by Core's
    AppInfo from the assembly's informational version, so a correct title also proves that Core call survives AOT.

    The screenshot uses PrintWindow with PW_RENDERFULLCONTENT, which captures GPU-composed windows like Avalonia's
    without bringing them to the front.

    Startup reads the machine before the main window appears (SHELL-5): PC info, the mod manager, the game path and the
    game INIs. With -Scenario the exe runs on that Parity Scenario's fake machine (its tree copied to a temp root, and
    CMT_PARITY_SCENARIO / CMT_PARITY_ROOT set), so the main window must appear wherever this runs. Without it the exe
    runs on the real machine, which proves the Win32 calls behind those reads survive AOT; on a machine where
    Fallout 4 can't be found (a CI runner), reaching the 'Fallout 4 Not Found' question also passes, since every read
    before it ran. That box is closed (which answers No) and nothing else is checked.

.PARAMETER Exe
    The published cm-toolkit.exe.

.PARAMETER Scenario
    A Parity Scenario folder (holding scenario.json and tree/) to run the exe on, such as
    ../parity/scenarios/shell/main-window.

.PARAMETER Title
    The title the main window must have.

.PARAMETER TimeoutSeconds
    How long to wait for the window.

.PARAMETER Screenshot
    If set, the PNG file to write the window capture to (outer window rect, including the title bar).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Exe,
    [string] $Title = 'Collective Modding Toolkit v0.6.2-dev',
    [int] $TimeoutSeconds = 30,
    [string] $Screenshot,
    [string] $Scenario
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CmtSmokeWin32 {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    public static string TitleOf(IntPtr hwnd) {
        var text = new StringBuilder(512);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }
}
'@

$exePath = (Resolve-Path -LiteralPath $Exe).ProviderPath
# Start from a scratch working directory: settings.json and cm-toolkit.log are CWD-relative, so a run from the
# publish folder would leave files beside the exe, and the exe-folder lookup of download-source.txt must not depend
# on the working directory anyway.
$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("cmt-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workDir | Out-Null
$startDir = $workDir
# The real machine's own question when it has no Fallout 4; never accepted on a scenario's machine.
$notFoundTitle = if ($Scenario) { $null } else { 'Fallout 4 Not Found' }
if (-not $Scenario) {
    # The update check runs before the main window shows (UPD-1). A smoke test must not reach the network, so the
    # real-machine run turns it off; the missing settings keys get their defaults. A scenario run serves its own
    # canned http/ responses (ScenarioHttpHandler) and fails on any request it has none for.
    Set-Content -LiteralPath (Join-Path $workDir 'settings.json') -Value '{"update_source": "none"}' -Encoding utf8NoBOM
}

if ($Scenario) {
    # Materialize the scenario as the parity harness does: the tree, then emptyDirs (attributes don't matter here).
    $scenarioDir = (Resolve-Path -LiteralPath $Scenario).ProviderPath
    $manifestPath = Join-Path $scenarioDir 'scenario.json'
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $tree = Join-Path $scenarioDir 'tree'
    if (Test-Path -LiteralPath $tree) {
        Copy-Item -Recurse -Path (Join-Path $tree '*') -Destination $workDir
    }
    foreach ($dir in @($manifest.emptyDirs)) {
        if ($dir) { New-Item -ItemType Directory -Force -Path (Join-Path $workDir $dir) | Out-Null }
    }
    $cwd = $manifest.host.cwd
    if ($cwd) {
        $startDir = if ($cwd.StartsWith('<ROOT>')) { $workDir + $cwd.Substring(6) } else { $cwd }
        New-Item -ItemType Directory -Force -Path $startDir | Out-Null
    }
    # Inherited by the exe, which then runs on ScenarioHostEnvironment instead of the real machine.
    $env:CMT_PARITY_SCENARIO = $manifestPath
    $env:CMT_PARITY_ROOT = $workDir
}

try {
    $process = Start-Process -FilePath $exePath -WorkingDirectory $startDir -PassThru
}
finally {
    Remove-Item Env:CMT_PARITY_SCENARIO, Env:CMT_PARITY_ROOT -ErrorAction SilentlyContinue
}
try {
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "cm-toolkit.exe exited with code $($process.ExitCode) before showing a window."
        }
        $windows = [CmtSmokeWin32]::VisibleWindowsOf([uint32]$process.Id)
        $hwnd = $windows | Where-Object { [CmtSmokeWin32]::TitleOf($_) -eq $Title } | Select-Object -First 1
        if ($hwnd) { break }
        $question = if ($notFoundTitle) { $windows | Where-Object { [CmtSmokeWin32]::TitleOf($_) -eq $notFoundTitle } | Select-Object -First 1 }
        if ($question) {
            Write-Host "OK: startup read the machine and asked '$notFoundTitle' (no Fallout 4 here); not checking the shell."
            # The box isn't the process's MainWindowHandle (it has no taskbar button), so close it directly: No, exit 0.
            [void][CmtSmokeWin32]::PostMessage($question, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
            return
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $hwnd) {
        $seen = ([CmtSmokeWin32]::VisibleWindowsOf([uint32]$process.Id) | ForEach-Object { "'$([CmtSmokeWin32]::TitleOf($_))'" }) -join ', '
        throw "No visible window titled '$Title' within $TimeoutSeconds s. Visible windows of the process: $seen"
    }

    # Give the first frame time to render before measuring and capturing.
    Start-Sleep -Seconds 2
    if ($process.HasExited) {
        throw "cm-toolkit.exe exited with code $($process.ExitCode) right after showing its window."
    }

    $client = New-Object CmtSmokeWin32+RECT
    [void][CmtSmokeWin32]::GetClientRect($hwnd, [ref]$client)
    $scale = [CmtSmokeWin32]::GetDpiForWindow($hwnd) / 96.0
    $expectedWidth = [math]::Round(760 * $scale)
    $expectedHeight = [math]::Round(450 * $scale)
    Write-Host "Window '$Title': client $($client.Right)x$($client.Bottom) px at scale $scale."
    if ($client.Right -ne $expectedWidth -or $client.Bottom -ne $expectedHeight) {
        throw "Client area is $($client.Right)x$($client.Bottom) px; expected ${expectedWidth}x${expectedHeight} (760x450 DIPs)."
    }

    $outer = New-Object CmtSmokeWin32+RECT
    [void][CmtSmokeWin32]::GetWindowRect($hwnd, [ref]$outer)
    if ($Screenshot) {
        $bitmap = New-Object System.Drawing.Bitmap ($outer.Right - $outer.Left), ($outer.Bottom - $outer.Top)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $hdc = $graphics.GetHdc()
                # PW_RENDERFULLCONTENT (2): needed for DirectComposition/GPU-rendered windows.
                [void][CmtSmokeWin32]::PrintWindow($hwnd, $hdc, 2)
                $graphics.ReleaseHdc($hdc)
            }
            finally {
                $graphics.Dispose()
            }
            # Resolved against PowerShell's location, which can differ from the process working directory.
            $shotPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Screenshot)
            New-Item -ItemType Directory -Force -Path (Split-Path $shotPath) | Out-Null
            $bitmap.Save($shotPath, [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally {
            $bitmap.Dispose()
        }
        Write-Host "Screenshot: $shotPath"
    }

    Write-Host 'OK: the shell started.'
}
finally {
    if (-not $process.HasExited) {
        # Close politely first so the window gets WM_CLOSE like a user close; kill only if it doesn't go.
        [void]$process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
        }
    }
    Remove-Item -Recurse -Force -LiteralPath $workDir -ErrorAction SilentlyContinue
}
