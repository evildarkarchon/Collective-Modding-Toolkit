# PROTOTYPE helper (throwaway): runs drive_python.py once per -Pick, waits for READY, screen-grabs the main
# window plus both docked panes as one composite, then kills the app.
# Needs a Tk 8.6 interpreter with the project deps (the uv-managed CPython ships Tk 9, which sv_ttk rejects):
#   $env:UV_PROJECT_ENVIRONMENT="$env:TEMP\cmt-ref\venv86"; uv sync --python C:\Python314\python.exe --frozen
# and a scratch CWD with an `assets` junction to src\assets (asset paths are CWD-relative).
param(
    [string[]]$Pick = @("", "Race Subgraph", "Invalid Archive"),
    [string]$Python = "$env:TEMP\cmt-ref\venv86\Scripts\python.exe",
    [string]$WorkDir = "$env:TEMP\cmt-ref"
)
Add-Type -AssemblyName System.Drawing
$driver = Join-Path $PSScriptRoot "drive_python.py"
foreach ($p in $Pick) {
    $log = Join-Path $WorkDir "drive.out"
    Remove-Item $log -ErrorAction SilentlyContinue
    $proc = Start-Process $Python -ArgumentList "`"$driver`"", "none", "`"$p`"" -WorkingDirectory $WorkDir -RedirectStandardOutput $log -PassThru -WindowStyle Normal
    for ($i = 0; $i -lt 240 -and -not ((Test-Path $log) -and (Select-String -Quiet -Path $log -Pattern "READY")); $i++) { Start-Sleep -Milliseconds 500 }
    Start-Sleep -Milliseconds 700
    $root = Select-String -Path $log -Pattern "^root\s+client=\((\d+),(\d+)\)" | Select-Object -Last 1
    $x = [int]$root.Matches[0].Groups[1].Value - 20; $y = [int]$root.Matches[0].Groups[2].Value - 50
    $bmp = New-Object System.Drawing.Bitmap 1000, 720
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size); $g.Dispose()
    $name = if ($p) { "python-scanner-" + ($p -replace '\W', '').ToLower() + ".png" } else { "python-scanner-composite.png" }
    $bmp.Save((Join-Path $PSScriptRoot $name)); $bmp.Dispose()
    Get-Content $log | Out-Host
    "  -> $name"
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 500
}
