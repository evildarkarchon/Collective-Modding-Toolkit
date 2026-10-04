# Parity assets

Synthetic inputs that git-committed text can't express, kept with the sources that build them
([ADR-0004](../../docs/adr/0004-parity-proved-by-recorded-scenarios.md)). No real game files are committed. Each
toolchain lands with the first slice that needs it:

| Toolchain | First consumer |
|---|---|
| Version-resourced exe/dll stubs (C/`.rc`, MSVC) | Overview binaries |
| Real-PE F4SE plugin DLLs (C, MSVC) | F4SE |
| CRC32-forged small files | Downgrader window |
| Self-made xdelta3 Delta Patches, served from a scenario's `http/` | Downgrader run |
| ESP/ESM/BA2 header authoring script (output committed, so it isn't on the test path) | Overview modules/archives |

Scenarios copy the built outputs into their `tree/` (or `http/`); tests never build them.
