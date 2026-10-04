# PROTOTYPE: launches the prototype once per variant/scenario, captures the main window to shots\, then exits.
# Usage (from this folder, after `dotnet build`): pwsh ./capture.ps1 [-Variants A,B,C] [-Scenarios baseline,problems]
param(
    [string[]]$Variants = @("A", "B", "C"),
    [string[]]$Scenarios = @("baseline", "problems"),
    [string]$TextMode = "Unspecified"
)
$exe = Join-Path $PSScriptRoot "bin\Debug\net10.0\CmtShellPrototype.exe"
$out = Join-Path $PSScriptRoot "shots"
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($s in $Scenarios) {
    foreach ($v in $Variants) {
        $p = Start-Process $exe -ArgumentList "--variant", $v, "--scenario", $s, "--textmode", $TextMode -PassThru
        Start-Sleep -Seconds 4
        & (Join-Path $PSScriptRoot "shot.ps1") -Pattern "^Collective Modding Toolkit v" -OutDir $out | Out-Host
        $src = Join-Path $out "Collective Modding Toolkit v0.6.2-dev.png"
        Move-Item -Force $src (Join-Path $out $(if ($TextMode -eq "Unspecified") { "avalonia-$s-$v.png" } else { "textmode-$TextMode-$s-$v.png" }))
        Stop-Process -Id $p.Id -Force
        Start-Sleep -Milliseconds 500
    }
}
