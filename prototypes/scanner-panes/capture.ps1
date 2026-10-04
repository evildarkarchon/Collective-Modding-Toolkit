# PROTOTYPE: launches the prototype per variant/pick with the main window's client origin at the same screen spot
# as the Python reference captures (908,526), screen-grabs the same 1000x720 region, then exits.
# Usage (from this folder, after `dotnet build`): pwsh ./capture.ps1 [-Variants A,B,C] [-Picks "Race Subgraph",...]
param(
    [string[]]$Variants = @("A", "B", "C"),
    [string[]]$Picks = @("Wrong Version", "Race Subgraph", "Invalid Archive")
)
Add-Type -AssemblyName System.Drawing
$exe = Join-Path $PSScriptRoot "bin\Debug\net10.0\CmtPanesPrototype.exe"
$out = Join-Path $PSScriptRoot "shots"
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($v in $Variants) {
    foreach ($p in $Picks) {
        # Outer = client - (8, 31) on Windows 11 at 100 %, so this puts the client origin at (908, 526).
        $proc = Start-Process $exe -ArgumentList "--variant", $v, "--pick", "`"$p`"", "--x", "900", "--y", "495" -PassThru
        Start-Sleep -Seconds 3
        $bmp = New-Object System.Drawing.Bitmap 1000, 720
        $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen(888, 476, 0, 0, $bmp.Size); $g.Dispose()
        $name = "avalonia-$v-" + ($p -replace '\W', '').ToLower() + ".png"
        $bmp.Save((Join-Path $out $name)); $bmp.Dispose()
        "  -> $name"
        Stop-Process -Id $proc.Id -Force
        Start-Sleep -Milliseconds 500
    }
}
