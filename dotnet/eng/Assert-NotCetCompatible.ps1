<#
.SYNOPSIS
    Fails if a PE file is marked CET shadow-stack compatible.

.DESCRIPTION
    ADR-0002 requires the published cm-toolkit.exe to be linked with /CETCOMPAT:NO (<CETCompat>false</CETCompat>).
    A CET-compatible exe launched from Mod Organizer 2.5.2 on a CPU with shadow stacks crashes before Main, because
    usvfs's injection stub returns with a ret the shadow stack never saw (issue #35).

    The flag lives in the PE's debug directory, in an IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS (type 20) entry whose
    first DWORD carries IMAGE_DLLCHARACTERISTICS_EX_CET_COMPAT (0x1). No such entry means not CET-compatible. This
    reads it with System.Reflection.PortableExecutable, so it needs PowerShell 7 but no VS developer shell or dumpbin.

.PARAMETER Path
    The PE file to check.

.EXAMPLE
    ./Assert-NotCetCompatible.ps1 artifacts/publish/cm-toolkit.exe
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Path
)

$ErrorActionPreference = 'Stop'

$ImageDebugTypeExDllCharacteristics = 20
$CetCompat = 0x1

$full = (Resolve-Path -LiteralPath $Path).ProviderPath
$stream = [System.IO.File]::OpenRead($full)
try {
    $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
    try {
        $exEntries = @($pe.ReadDebugDirectory() | Where-Object { [int]$_.Type -eq $ImageDebugTypeExDllCharacteristics })
        $flags = 0
        foreach ($entry in $exEntries) {
            # Read the DWORD from the file offset, which is valid whether or not the section is mapped.
            $buffer = [byte[]]::new(4)
            $stream.Position = $entry.DataPointer
            if ($stream.Read($buffer, 0, 4) -ne 4) {
                throw "Truncated EX_DLLCHARACTERISTICS entry in $full."
            }
            $flags = $flags -bor [System.BitConverter]::ToUInt32($buffer, 0)
        }
    }
    finally {
        $pe.Dispose()
    }
}
finally {
    $stream.Dispose()
}

if ($flags -band $CetCompat) {
    Write-Error "$full is marked CET-compatible (EX_DLLCHARACTERISTICS 0x$($flags.ToString('X'))). It must be linked with <CETCompat>false</CETCompat> (ADR-0002)."
    exit 1
}

Write-Host "OK: $full is not CET-compatible (EX_DLLCHARACTERISTICS entries: $($exEntries.Count), flags 0x$($flags.ToString('X')))."
