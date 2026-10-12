# Parity assets

Synthetic inputs that git-committed text can't express, kept with the sources that build them
([ADR-0004](../../docs/adr/0004-parity-proved-by-recorded-scenarios.md)). No real game files are committed. Each
toolchain lands with the first slice that needs it:

| Toolchain | First consumer |
|---|---|
| Version-resourced exe/dll stubs (C/`.rc`, MSVC): [`version-stubs/`](version-stubs/build.ps1), outputs in `version-stubs/out/<set>/` | Overview binaries |
| Real-PE F4SE plugin DLLs (C, MSVC): [`f4se-plugins/`](f4se-plugins/build.ps1), one per export/version-struct variant plus a 32-bit one, outputs in `f4se-plugins/out/<arch>/` | F4SE |
| CRC32-forged small files: the forger [`crc32-forge/forge_crc32.py`](crc32-forge/forge_crc32.py) landed early with the Overview binaries, for CRC-keyed binaries and the Down-Grade `Startup.ba2`; [`crc32-forge/build_downgrader.py`](crc32-forge/build_downgrader.py) writes the Downgrader scenarios' installs to `crc32-forge/out/downgrader/<set>/` and a stand-in for every Downgrader table CRC to `out/downgrader/all/` | Downgrader window |
| Self-made xdelta3 Delta Patches: [`delta-patches/build_delta_patches.py`](delta-patches/build_delta_patches.py) runs `xdelta3 -e -S none` (VCDiff can't decode DJW/FGK secondary compression) between the CRC32-forged stand-ins, both directions for all six files, plus a 64 KiB multi-window launcher pair and a truncated copy of its patch in `out/big/`; scenarios copy them into `http/delta/` (or `Cwd/`) | Downgrader run |
| ESP/ESM/BA2 header authoring script [`plugin-headers/author_headers.py`](plugin-headers/author_headers.py): writes the headers (and the limit scenarios' long INI and `plugins.txt` lists) straight into the `overview/` scenario trees, where they are committed, so it isn't on the test path; [`plugin-headers/author_archive_patcher.py`](plugin-headers/author_archive_patcher.py) reuses it for the `archive-patcher/` trees (archives, their INI lists and `Changes/` overlays) | Overview modules/archives |

Scenarios copy the built outputs into their `tree/` (or `http/`); tests never build them.
