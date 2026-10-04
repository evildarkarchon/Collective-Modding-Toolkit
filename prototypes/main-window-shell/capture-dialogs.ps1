# PROTOTYPE: launches the prototype once per comparison dialog (--open) and captures the dialog window into shots\.
# The tooltip is an untitled popup, so for it the screen area around the main window is grabbed instead.
param([string[]]$Dialogs = @("msg-custom", "msg-win32", "msg-msbox", "log-colour", "log-mono", "tree", "tooltip"))
$exe = Join-Path $PSScriptRoot "bin\Debug\net10.0\CmtShellPrototype.exe"
$out = Join-Path $PSScriptRoot "shots"
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
foreach ($d in $Dialogs) {
    $p = Start-Process $exe -ArgumentList "--variant", "C", "--open", $d -PassThru
    Start-Sleep -Seconds 4
    if ($d -eq "tooltip") {
        $main = Get-Process -Id $p.Id
        & (Join-Path $PSScriptRoot "shot.ps1") -Pattern "^Collective Modding Toolkit v" -List | Out-Null
        # Grab the primary-screen region the window was centred in (CenterScreen), padded for the popup.
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $w = 1000; $h = 640
        $x = $b.X + [int](($b.Width - $w) / 2); $y = $b.Y + [int](($b.Height - $h) / 2)
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
        $g.Dispose()
        $bmp.Save((Join-Path $out "dialog-tooltip.png")); $bmp.Dispose()
    }
    else {
        & (Join-Path $PSScriptRoot "shot.ps1") -Pattern "^(Warning|Downgrader|Detected Invalid Module Versions)$" -OutDir $out | Out-Host
        foreach ($t in "Warning", "Downgrader", "Detected Invalid Module Versions") {
            $src = Join-Path $out "$t.png"
            if (Test-Path $src) { Move-Item -Force $src (Join-Path $out "dialog-$d.png") }
        }
    }
    Stop-Process -Id $p.Id -Force
    Start-Sleep -Milliseconds 500
}
