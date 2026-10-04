<#
.SYNOPSIS
    Starts the published exe, waits for the shell window, checks it, optionally screenshots it, and closes it.

.DESCRIPTION
    A smoke test for the NativeAOT build. Some AOT failures only show up at runtime: a XAML include loaded by URI
    throws "No precompiled XAML found" before any window appears, for example. Headless tests run on the JIT, so they
    can't catch those.

    It checks that a visible top-level window owned by the process appears with the expected title, and that its
    client area is 760x450 at 100% scaling (scaled by the window's DPI otherwise). At 100% it also checks the outer
    window rect starts where the Reference Implementation puts it: (screen // 2 - 380, screen // 2 - 225) on the
    primary screen (SHELL-6). The title is built by Core's AppInfo from the assembly's informational version, so a
    correct title also proves that Core call survives AOT.

    The screenshot uses PrintWindow with PW_RENDERFULLCONTENT, which captures GPU-composed windows like Avalonia's
    without bringing them to the front.

.PARAMETER Exe
    The published cm-toolkit.exe.

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
    [string] $Screenshot
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
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);

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

$process = Start-Process -FilePath $exePath -WorkingDirectory $workDir -PassThru
try {
    $hwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "cm-toolkit.exe exited with code $($process.ExitCode) before showing a window."
        }
        $hwnd = [CmtSmokeWin32]::VisibleWindowsOf([uint32]$process.Id) |
            Where-Object { [CmtSmokeWin32]::TitleOf($_) -eq $Title } |
            Select-Object -First 1
        if ($hwnd) { break }
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
    if ($scale -eq 1) {
        # SM_CXSCREEN (0) / SM_CYSCREEN (1): the primary screen's full size. Only checked at 100%, where logical and
        # physical pixels agree; the scaled mapping is covered by ShellPlacementTests.
        $expectedLeft = [math]::Floor([CmtSmokeWin32]::GetSystemMetrics(0) / 2) - 380
        $expectedTop = [math]::Floor([CmtSmokeWin32]::GetSystemMetrics(1) / 2) - 225
        Write-Host "Outer window rect starts at ($($outer.Left), $($outer.Top))."
        if ($outer.Left -ne $expectedLeft -or $outer.Top -ne $expectedTop) {
            throw "Window starts at ($($outer.Left), $($outer.Top)); the reference's formula gives ($expectedLeft, $expectedTop)."
        }
    }

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
