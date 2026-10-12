<#
.SYNOPSIS
    Builds the F4SE plugin stand-ins that the F4SE scenarios commit (F4SE-2..F4SE-6, T-5, B-18).

.DESCRIPTION
    Each stub is a tiny real PE built with MSVC from plugin.c, with exactly the exports and F4SEPlugin_Version struct
    of one row below. The F4SE tab maps each DLL with LoadLibraryExW(DONT_RESOLVE_DLL_REFERENCES) (T-5: a real loader,
    not a managed PE reader), so the stubs are real DLLs; one is built for x86, which a 64-bit process can't load
    (B-18). The outputs land in out/<arch>/ and are committed; scenarios copy them into their tree/, and nothing on the
    test path builds them.

    /Brepro makes the link reproducible, so a rebuild with the same toolset gives the same bytes. A different MSVC
    version may emit different code, which changes no golden (the goldens hold no hashes of these files).

    Needs the VS Build Tools' C++ workload (x64 and x86 tools), found through vswhere.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$here = $PSScriptRoot
$out = Join-Path $here 'out'
$work = Join-Path ([IO.Path]::GetTempPath()) "cmt-f4se-plugins-$PID"

# Runtime versions as F4SE packs them (MAKE_EXE_VERSION: major << 24 | minor << 16 | build << 4).
$ng980 = '0x010A3D40'   # 1.10.980, the first Next-Gen build; the reference's Next-Gen marker
$ng984 = '0x010A3D80'   # 1.10.984, the other Next-Gen marker
$ae137 = '0x010B0890'   # 1.11.137, the first Anniversary build; Anniversary support is "any version above it"
$ae191 = '0x010B0BF0'   # 1.11.191

# One row per stub: the architecture, the file name, its F4SE exports, and for those exporting F4SEPlugin_Version the
# addressIndependence and structureIndependence flags and the compatibleVersions entries (unlisted entries are 0).
# The reference masks both flags with 0b110 (bit 0 is "signatures"/"no structs" and doesn't count) and reads all 16
# compatibleVersions entries, so some rows put a version after a 0 or above 0x7FFFFFFF (an unsigned compare, T-4).
$stubs = @(
    @{ Arch = 'x64'; Name = 'OldGenOnly.dll'; Exports = 'QUERY', 'LOAD' }
    @{ Arch = 'x64'; Name = 'PreloadAndQuery.dll'; Exports = 'PRELOAD', 'QUERY' }
    @{ Arch = 'x64'; Name = 'NextGenAndAnniversary.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = $ng984, $ae191 }
    @{ Arch = 'x64'; Name = 'NextGenOnly.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = , $ng980 }
    @{ Arch = 'x64'; Name = 'AnniversaryFirstRelease.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = , $ae137 }
    @{ Arch = 'x64'; Name = 'CompatibleAfterGap.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = $ng980, '0', '0', $ae191 }
    @{ Arch = 'x64'; Name = 'HighVersion.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = , '0xFF000000' }
    @{ Arch = 'x64'; Name = 'NoCompatibleVersions.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 0; Versions = @() }
    # Address Library for 1.11.137+ (1 << 2) and the 1.11.137 struct layout (1 << 2): independent.
    @{ Arch = 'x64'; Name = 'AddressLibrary.dll'; Exports = 'LOAD', 'VERSION'; Address = 4; Structure = 4; Versions = , $ae191 }
    @{ Arch = 'x64'; Name = 'AllVersions.dll'; Exports = 'QUERY', 'LOAD', 'VERSION'; Address = 2; Structure = 2; Versions = , $ng984 }
    @{ Arch = 'x64'; Name = 'AddressIndependentOnly.dll'; Exports = 'LOAD', 'VERSION'; Address = 2; Structure = 0; Versions = , $ae191 }
    @{ Arch = 'x64'; Name = 'StructureIndependentOnly.dll'; Exports = 'LOAD', 'VERSION'; Address = 0; Structure = 4; Versions = , $ng984 }
    # Signatures (1 << 0) and no structs (1 << 0) only: both masked away, so neither counts as independent.
    @{ Arch = 'x64'; Name = 'SignaturesOnly.dll'; Exports = 'PRELOAD', 'VERSION'; Address = 1; Structure = 1; Versions = @() }
    # Supports things, but neither F4SEPlugin_Load nor F4SEPlugin_Preload: not an F4SE plugin.
    @{ Arch = 'x64'; Name = 'QueryAndVersionNoLoad.dll'; Exports = 'QUERY', 'VERSION'; Address = 2; Structure = 2; Versions = , $ng984 }
    @{ Arch = 'x64'; Name = 'NotAPlugin.dll'; Exports = , 'OTHER' }
    @{ Arch = 'x64'; Name = 'NoExports.dll'; Exports = @() }
    # A real plugin, but for 32-bit: LoadLibraryExW fails with ERROR_BAD_EXE_FORMAT in a 64-bit process (B-18).
    @{ Arch = 'x86'; Name = 'OldGenOnly.dll'; Exports = 'QUERY', 'LOAD' }
)

function Import-VcVars {
    <# Loads the MSVC developer environment for $Arch ('x64', or 'x64_x86' for the x86 cross tools) into this session. #>
    param([string] $Arch)
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'No Visual Studio with the C++ x64/x86 tools was found.' }
    $vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvarsall.bat'
    # vcvarsall.bat runs vswhere itself and complains when it isn't on PATH, which it isn't by default.
    $env:PATH += ";$(Split-Path $vswhere)"
    # `set` after vcvarsall prints the resulting environment; the banner lines before it have no '=' and are skipped.
    foreach ($line in (& cmd.exe /d /c "`"$vcvars`" $Arch && set")) {
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

function New-VariantHeader {
    <# Writes the variant.h that selects one stub's exports and version struct (see plugin.c). #>
    param([string] $Path, [hashtable] $Stub)
    $lines = @("/* Generated by build.ps1 for $($Stub.Arch)/$($Stub.Name). */")
    foreach ($export in $Stub.Exports) { $lines += "#define EXPORT_$export" }
    if ($Stub.Exports -contains 'VERSION') {
        $versions = if ($Stub.Versions.Count -gt 0) { $Stub.Versions -join ', ' } else { '0' }
        $lines += "#define PLUGIN_NAME `"$([IO.Path]::GetFileNameWithoutExtension($Stub.Name))`""
        $lines += "#define ADDRESS_INDEPENDENCE $($Stub.Address)"
        $lines += "#define STRUCTURE_INDEPENDENCE $($Stub.Structure)"
        $lines += "#define COMPATIBLE_VERSIONS $versions"
    }
    [IO.File]::WriteAllLines($Path, $lines)
}

New-Item -ItemType Directory -Force $work | Out-Null
$saved = [Environment]::GetEnvironmentVariables()
try {
    foreach ($arch in 'x64', 'x86') {
        # Each architecture gets a fresh developer environment: vcvarsall appends to PATH, LIB and INCLUDE.
        foreach ($name in @([Environment]::GetEnvironmentVariables().Keys)) {
            if (-not $saved.Contains($name)) { [Environment]::SetEnvironmentVariable($name, $null) }
        }
        foreach ($entry in $saved.GetEnumerator()) { [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value) }
        Import-VcVars $(if ($arch -eq 'x64') { 'x64' } else { 'x64_x86' })
        $machine = if ($arch -eq 'x64') { 'X64' } else { 'X86' }

        foreach ($stub in $stubs | Where-Object { $_.Arch -eq $arch }) {
            $dir = Join-Path $out $arch
            New-Item -ItemType Directory -Force $dir | Out-Null
            $stubWork = Join-Path $work "$arch-$([IO.Path]::GetFileNameWithoutExtension($stub.Name))"
            New-Item -ItemType Directory -Force $stubWork | Out-Null
            New-VariantHeader (Join-Path $stubWork 'variant.h') $stub

            $obj = Join-Path $stubWork 'plugin.obj'
            # /GS- and no CRT: nothing here runs. /Brepro: reproducible object.
            Invoke-Tool 'cl.exe' @('/nologo', '/c', '/O1', '/GS-', '/Brepro', "/I$stubWork", "/Fo$obj", (Join-Path $here 'plugin.c'))
            # Never run, so no entry point. The import library and .exp the exports produce go to the work folder.
            Invoke-Tool 'link.exe' @(
                '/nologo', '/Brepro', '/NODEFAULTLIB', "/MACHINE:$machine", '/DLL', '/NOENTRY', '/OPT:REF',
                "/IMPLIB:$(Join-Path $stubWork 'plugin.lib')", "/OUT:$(Join-Path $dir $stub.Name)", $obj)
        }
    }
} finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

Write-Host "Built $($stubs.Count) stubs under $out"
