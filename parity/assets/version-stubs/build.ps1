<#
.SYNOPSIS
    Builds the version-resourced stand-ins for the game's binaries that the Overview scenarios commit (OVW-B1..B8).

.DESCRIPTION
    Each stub is a tiny x64 PE built with MSVC from stub.c and version.rc, carrying only the file version the
    Reference Implementation looks up in BASE_FILES (globals.py). A stub that BASE_FILES knows only by CRC32 is then
    forged to that CRC with ../crc32-forge/forge_crc32.py, and so is the Down-Grade "Fallout4 - Startup.ba2", whose
    CRC skips its 12-byte header (OVW-B6). The outputs land in out/<set>/ and are committed; scenarios copy them into
    their tree/, and nothing on the test path builds them.

    /Brepro makes the link reproducible, so a rebuild with the same toolset gives the same bytes (and the same CRCs
    in the goldens). A different MSVC version may emit different code, and goldens can't be re-recorded since the
    Cutover (#71), so don't commit a rebuild unless the Overview replays still pass with it.

    Needs the VS Build Tools' C++ workload and the Windows SDK (rc.exe), found through vswhere, and a python on PATH
    for the CRC forging.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$forge = Join-Path $here '..\crc32-forge\forge_crc32.py'
$out = Join-Path $here 'out'
$work = Join-Path ([IO.Path]::GetTempPath()) "cmt-version-stubs-$PID"

# One row per output: the set folder, the file name, the four version parts (or $null for no version resource),
# whether it is a DLL, and the CRC32 to forge (or $null). The sets mirror the install types the scenarios model;
# 'BA2' rows are not PEs (see New-StartupBa2).
$stubs = @(
    # Old-Gen 1.10.163: every BASE_FILES entry matches. Archive2 has no version resource, so it is matched by CRC
    # with the CRC as its "version string" (OVW-B2); the launcher has a version BASE_FILES doesn't list, so its
    # lookup falls through to the CRC.
    @{ Set = 'og'; Name = 'Fallout4.exe'; Version = 1, 10, 163, 0 }
    @{ Set = 'og'; Name = 'Fallout4Launcher.exe'; Version = 1, 0, 0, 1; Crc = '02445570' }
    @{ Set = 'og'; Name = 'steam_api64.dll'; Version = 2, 89, 45, 4; Dll = $true }
    @{ Set = 'og'; Name = 'f4se_loader.exe'; Version = 0, 0, 6, 23 }
    @{ Set = 'og'; Name = 'f4se_steam_loader.dll'; Version = 0, 0, 6, 23; Dll = $true }
    @{ Set = 'og'; Name = 'CreationKit.exe'; Version = 1, 10, 162, 0 }
    @{ Set = 'og'; Name = 'Archive2.exe'; Version = $null; Crc = '4CDFC7B5' }
    @{ Set = 'og'; Name = 'Fallout4 - Startup.ba2'; Ba2 = $true }
    # The Down-Grade marker: the Next-Gen Startup.ba2's CRC (globals.NG_STARTUP_BA2_CRC) past the header.
    @{ Set = 'dg'; Name = 'Fallout4 - Startup.ba2'; Ba2 = $true; Crc = 'A5808F5F' }
    # Next-Gen 1.10.984.
    @{ Set = 'ng'; Name = 'Fallout4.exe'; Version = 1, 10, 984, 0 }
    @{ Set = 'ng'; Name = 'Fallout4Launcher.exe'; Version = 1, 0, 0, 1; Crc = 'F6A06FF5' }
    @{ Set = 'ng'; Name = 'steam_api64.dll'; Version = 7, 40, 51, 27; Dll = $true }
    @{ Set = 'ng'; Name = 'f4se_loader.exe'; Version = 0, 0, 7, 2 }
    @{ Set = 'ng'; Name = 'CreationKit.exe'; Version = 1, 10, 982, 3 }
    @{ Set = 'ng'; Name = 'Archive2.exe'; Version = 1, 0, 0, 1; Crc = '71A5240B' }
    # Anniversary 1.11.240 (steam_api64 is the Next-Gen one: BASE_FILES has one entry for both).
    @{ Set = 'ae'; Name = 'Fallout4.exe'; Version = 1, 11, 240, 0 }
    @{ Set = 'ae'; Name = 'Fallout4Launcher.exe'; Version = 1, 0, 0, 1; Crc = '57D1C794' }
    @{ Set = 'ae'; Name = 'f4se_loader.exe'; Version = 0, 0, 7, 9 }
    @{ Set = 'ae'; Name = 'CreationKit.exe'; Version = 1, 11, 240, 0 }
    @{ Set = 'ae'; Name = 'Archive2.exe'; Version = 1, 0, 0, 1; Crc = '9A059211' }
    # An Obsolete game (1.10.980, a Next-Gen build superseded by 1.10.984).
    @{ Set = 'obsolete'; Name = 'Fallout4.exe'; Version = 1, 10, 980, 0 }
    # Versions BASE_FILES doesn't know: Unknown.
    @{ Set = 'unknown'; Name = 'Fallout4.exe'; Version = 1, 10, 999, 0 }
    @{ Set = 'unknown'; Name = 'f4se_loader.exe'; Version = 0, 0, 9, 9 }
    # GOG's steam_api64 is known only by its CRC.
    @{ Set = 'gog'; Name = 'steam_api64.dll'; Version = 1, 0, 0, 0; Dll = $true; Crc = 'BD3AA35F' }
    # A Fallout4.exe with no version resource (version None, T-4) and one whose resource really is 0.0.0.0, which
    # Python treats as a version like any other.
    @{ Set = 'no-version'; Name = 'Fallout4.exe'; Version = $null }
    @{ Set = 'zero-version'; Name = 'Fallout4.exe'; Version = 0, 0, 0, 0 }
)

function Import-VcVars {
    <# Loads the x64 MSVC developer environment (vcvars64.bat) into this PowerShell session. #>
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'No Visual Studio with the C++ x64 tools was found.' }
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'
    # vcvars64.bat runs vswhere itself and complains when it isn't on PATH, which it isn't by default.
    $env:PATH += ";$(Split-Path $vswhere)"
    # `set` after vcvars prints the resulting environment; the banner lines before it have no '=' and are skipped.
    foreach ($line in (& cmd.exe /d /c "`"$vcvars`" && set")) {
        $at = $line.IndexOf('=')
        if ($at -gt 0) { [Environment]::SetEnvironmentVariable($line.Substring(0, $at), $line.Substring($at + 1)) }
    }
}

function Invoke-Tool {
    <# Runs a native tool and fails the build on a non-zero exit code. #>
    param([string] $Exe, [string[]] $Arguments)
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Exe failed with exit code $LASTEXITCODE" }
}

function New-StartupBa2 {
    <#
    A "Fallout4 - Startup.ba2" stand-in: a valid v1 GNRL BA2 header (BTDX, version 1, GNRL; what the Overview's
    archive count reads, OVW-A4) followed by a label. Only the Down-Grade one is CRC-forged, past the header.
    #>
    param([string] $Path, [string] $Label)
    $header = [byte[]](0x42, 0x54, 0x44, 0x58, 0x01, 0x00, 0x00, 0x00, 0x47, 0x4E, 0x52, 0x4C)
    $text = [Text.Encoding]::ASCII.GetBytes("Collective Modding Toolkit parity stub (not a game file): $Label`n")
    [IO.File]::WriteAllBytes($Path, $header + $text)
}

Import-VcVars
New-Item -ItemType Directory -Force $work | Out-Null
try {
    $obj = Join-Path $work 'stub.obj'
    # /GS- and no CRT: the entry point is the whole program. /Brepro: reproducible object.
    Invoke-Tool 'cl.exe' @('/nologo', '/c', '/O1', '/GS-', '/Brepro', "/Fo$obj", (Join-Path $here 'stub.c'))

    foreach ($stub in $stubs) {
        $dir = Join-Path $out $stub.Set
        New-Item -ItemType Directory -Force $dir | Out-Null
        $target = Join-Path $dir $stub.Name

        if ($stub.ContainsKey('Ba2')) {
            New-StartupBa2 $target $(if ($stub.ContainsKey('Crc')) { 'Down-Grade' } else { 'Old-Gen' })
            if ($stub.ContainsKey('Crc')) { Invoke-Tool 'python' @($forge, $target, $stub.Crc, '--skip', '12') }
            continue
        }

        $isDll = $stub.ContainsKey('Dll') -and $stub.Dll
        $inputs = @($obj)
        if ($null -ne $stub.Version) {
            $res = Join-Path $work "$($stub.Set)-$($stub.Name).res"
            $v = $stub.Version
            $defines = @('/d', "VER_MAJOR=$($v[0])", '/d', "VER_MINOR=$($v[1])", '/d', "VER_BUILD=$($v[2])", '/d', "VER_PRIVATE=$($v[3])")
            if ($isDll) { $defines += @('/d', 'STUB_DLL') }
            Invoke-Tool 'rc.exe' (@('/nologo') + $defines + @('/fo', $res, (Join-Path $here 'version.rc')))
            $inputs += $res
        }

        $link = @('/nologo', '/Brepro', '/NODEFAULTLIB', '/MACHINE:X64', '/OPT:REF', "/OUT:$target")
        if ($isDll) {
            # Never loaded, so no entry point; no exports either, so no import library is written.
            $link += @('/DLL', '/NOENTRY')
        } else {
            $link += @('/ENTRY:StubMain', '/SUBSYSTEM:CONSOLE')
        }
        Invoke-Tool 'link.exe' ($link + $inputs)

        if ($stub.ContainsKey('Crc')) { Invoke-Tool 'python' @($forge, $target, $stub.Crc) }
    }
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host "Built $($stubs.Count) stubs under $out"
