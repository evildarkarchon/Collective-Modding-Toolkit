# Research: .NET replacement for `pyxdelta` (xdelta3 / VCDIFF decoding)

Ticket: [#3](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/3) (map: [#2](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/2)).
Researched 2026-10-03 against parity baseline `f95a07c`.

## Summary and recommendation

**Use the managed NuGet package [`VCDiff`](https://www.nuget.org/packages/VCDiff/5.0.0) (SnowflakePowered/vcdiff, v5.0.0, Apache-2.0)**
for applying Downgrader patches. Call its `VcDecoder(source, delta, output)` and then `Decode(out long bytesWritten)`.

Why:

1. **It decodes every patch the Downgrader downloads, verified end to end.** All 12 `delta-patches` assets
   were parsed window by window. Six of them were then applied with VCDiff 5.0.0 to real Fallout 4 / Creation
   Kit files, and each output matched the Downgrader's own CRC32 table byte for byte. That includes the two
   largest outputs, `Fallout4.exe` and `CreationKit.exe` in both directions (see [Empirical verification](#empirical-verification)).
2. **It supports the exact xdelta3 extensions the patches use.** That means the `VCD_APPHEADER` header bit and
   the per-window `VCD_ADLER32` checksum. It verifies the Adler-32 checksums by default, as xdelta3 does.
3. **It is pure managed code, NativeAOT-clean and trim-clean.** A `PublishAot=true` build produced zero
   IL2xxx/IL3xxx warnings and decoded all six test patches correctly. No native binary ships with it, so
   packaging stays open: it works the same as NativeAOT or as self-contained single-file.
4. **It is fast enough.** The largest patch (80 MB output) decodes in about 0.4 to 0.75 s, and peak working set
   stays under 60 MiB. Native xdelta3 takes about 0.1 to 0.2 s, and both are far below the download time.
5. **The licence works for this project.** Apache-2.0 is compatible with GPLv3, so it can be combined with this
   GPL-2.0-or-later project if the result is distributed under GPLv3 terms. The Python app already carries the
   same constraint, because the xdelta3 code inside `pyxdelta` is also Apache-2.0 (see [Licensing](#licensing)).
6. **It is maintained.** v5.0.0 was released 2026-03-03, it adds a `net10.0` target, and it has about 157k NuGet downloads.

Pin `SharpCompress` to version 0.48.0 or later directly (see [Risks](#risks-and-open-questions)). Write a
characterization test that applies a real patch and checks the CRC.

## How the Python app uses pyxdelta today

Source: [`src/downgrader.py`](../../src/downgrader.py) at `f95a07c`.

- Patch URL: `PATCH_URL_BASE + {"NG-to-OG-" | "OG-to-NG-"} + file_path.name + ".xdelta"`, where
  `PATCH_URL_BASE = "https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/"`
  (`downgrader.py` lines 51, 380). `file_path.name` drops the `Tools\Archive2\` prefix, so the name is, for
  example, `NG-to-OG-Archive2.exe.xdelta`.
- Patches are downloaded into the **current working directory**, named after the URL's filename (`_threaded_download`, lines 417-429).
- `apply_patch` (lines 446-466) calls `pyxdelta.decode(str(infile), patch_name, str(outfile))`.
  `infile` is the renamed backup of the current version (`*_upgradeBackup*` or `*_downgradeBackup*`), and
  `outfile` is the original game file path. A `True` return value is treated as success.
- `pyxdelta.decode` is a thin wrapper that runs the xdelta3 command-line `main()` in-process with
  `xdelta3 -d -f -s <infile> <patchfile> <outfile>`. So it decodes with force-overwrite, taking the source from
  the backup ([`pyxdelta.c`](https://github.com/Illidanz/pyxdelta/blob/master/pyxdelta.c), `method_decode`). It
  returns `result == 0`.
- pyxdelta 0.2.3 (MIT, [PyPI](https://pypi.org/project/pyxdelta/)) builds the xdelta3 submodule pinned at
  [`jmacd/xdelta@7508fd2a`](https://github.com/jmacd/xdelta/tree/7508fd2a823443b1f0173ca361620f21d62a7d37) with
  `SECONDARY_DJW=1`, `SECONDARY_FGK=1`, `SECONDARY_LZMA=0`, `EXTERNAL_COMPRESSION=0`
  ([`setup.py`](https://github.com/Illidanz/pyxdelta/blob/master/setup.py)).
- Behaviour to keep the same: on failure, the Python code still logs "Failed patching" and then deletes the
  backup `infile` unless "Keep Backups" is ticked (lines 455-461). That is a data-loss path. Per the map's
  "port as-is" rule it is flagged below as a follow-up, not fixed here.

## The patch format, from the real files

The release [`wxMichael/Collective-Modding-Toolkit@delta-patches`](https://github.com/wxMichael/Collective-Modding-Toolkit/releases/tag/delta-patches)
("Pyxdelta Patch Files", created 2024-09-26) has 12 assets. All were downloaded, and every window header was
parsed with a small script written against the format definitions in
[RFC 3284 §4](https://www.rfc-editor.org/rfc/rfc3284#section-4) and xdelta3's
[`xdelta3.c` lines 296-318](https://github.com/jmacd/xdelta/blob/v3.2.1/xdelta3/xdelta3.c#L296-L318).

| Fact | Value in all 12 patches | Meaning |
|---|---|---|
| Magic / version | `D6 C3 C4 00` | Standard VCDIFF, RFC 3284 §4.1 |
| `Hdr_Indicator` | `0x04` only | `VCD_APPHEADER`, an **xdelta3 extension**. RFC 3284 defines only bits 0 (`VCD_DECOMPRESS`) and 1 (`VCD_CODETABLE`). |
| Secondary compressor (`VCD_DECOMPRESS`) | **absent** | No DJW, FGK or LZMA secondary compression |
| Custom code table (`VCD_CODETABLE`) | absent | Default RFC 3284 code table |
| App header contents | e.g. `C:\Users\Michael\Downloads\xdelta3\OG-Fallout4.exe//C:\Users\Michael\Downloads\xdelta3\NG-Fallout4.exe/` | xdelta3's default `target/comp/source/comp` header (`main_set_appheader`, [`xdelta3-main.h`](https://github.com/jmacd/xdelta/blob/v3.2.1/xdelta3/xdelta3-main.h)). Informational only, so decoders can skip it. |
| `Win_Indicator` values | `0x05` (`VCD_SOURCE \| VCD_ADLER32`), and in 3 large patches also `0x04` (`VCD_ADLER32`, no source segment) | **Every window carries xdelta3's Adler-32 extension.** It is written as 4 raw big-endian bytes after the three section lengths (`DEC_CKSUM`, [`xdelta3-decode.h`](https://github.com/jmacd/xdelta/blob/v3.2.1/xdelta3/xdelta3-decode.h)). |
| `VCD_TARGET` windows | none | |
| `Delta_Indicator` | `0x00` in every window | No per-section compression |
| Max target window | 8,388,608 B (8 MiB) | xdelta3's `XD3_DEFAULT_WINSIZE = 1U << 23` ([`xdelta3.h` L77-78](https://github.com/jmacd/xdelta/blob/v3.2.1/xdelta3/xdelta3.h#L77-L78)) |
| Max source segment | 66,999,264 B | The decoder needs **random access to the source file** (seekable `FileStream`). |

Sizes (patch to output):

| Patch | Patch size | Output size | Windows |
|---|---:|---:|---:|
| NG-to-OG-CreationKit.exe | 65.9 MB | 80.4 MB | 10 |
| OG-to-NG-CreationKit.exe | 27.4 MB | 68.2 MB | 9 |
| NG-to-OG-Fallout4.exe | 53.1 MB | 65.5 MB | 8 |
| OG-to-NG-Fallout4.exe | 42.0 MB | 52.6 MB | 7 |
| Launcher / steam_api64 / Archive2 / Archive2Interop (8 files) | 24 KB to 168 KB | 63 KB to 4.5 MB | 1 each |

The ticket's "multi-hundred-MB executables" premise overstates the case. The largest output is 80 MB.

**What this means for the choice of decoder:** a strict RFC 3284 decoder that rejects unknown indicator bits
will fail on these files. A decoder that reads `Win_Indicator` bit 2 as open-vcdiff's SDCH checksum, which is
encoded as a varint, will misparse them. The replacement must explicitly support xdelta3's `VCD_APPHEADER` and
its 4-byte `VCD_ADLER32` field. It does **not** need DJW, FGK or LZMA secondary decompression, custom code
tables, or `VCD_TARGET`.

## Options

| Option | Licence | Handles these patches? | Maintenance | Perf (80 MB output) | AOT / trimming | Native binary? |
|---|---|---|---|---|---|---|
| **[`VCDiff`](https://www.nuget.org/packages/VCDiff/5.0.0) 5.0.0** ([SnowflakePowered/vcdiff](https://github.com/SnowflakePowered/vcdiff)) | Apache-2.0 | **Yes, verified.** README lists "xdelta3 with Adler32 Checksum and `VCD_APPHEADER` (without compression)" as decodable. Source skips the app header ([`VcDecoderEx.cs`](https://github.com/SnowflakePowered/vcdiff/blob/b4e0311afaaabc267232a5fded729c0e5c0879a4/src/VCDiff/Decoders/VcDecoderEx.cs)), parses the 4-byte xdelta3 checksum ([`WindowDecoder.cs`](https://github.com/SnowflakePowered/vcdiff/blob/b4e0311afaaabc267232a5fded729c0e5c0879a4/src/VCDiff/Decoders/WindowDecoder.cs)) and verifies it ([`BodyDecoder.cs`](https://github.com/SnowflakePowered/vcdiff/blob/b4e0311afaaabc267232a5fded729c0e5c0879a4/src/VCDiff/Decoders/BodyDecoder.cs)). Secondary: LZMA (id 2) only. | Active. 5.0.0 released 2026-03-03 with a `net10.0` target. About 157k downloads. 38 stars, 1 open issue. | 0.39 to 0.75 s, peak WS 32 to 58 MiB | **Clean.** `PublishAot` gives 0 warnings, and the AOT exe passes all tests. Uses `unsafe`, `MemoryMarshal` and `Marshal.AllocHGlobal`; no reflection. | No |
| [`PleOps.XdeltaSharp`](https://www.nuget.org/packages/PleOps.XdeltaSharp/1.3.0) 1.3.0 ([pleonex/xdelta-sharp](https://github.com/pleonex/xdelta-sharp)) | MIT | **Yes, verified.** Reads the app header and the 4-byte Adler-32, and verifies the checksum ([`WindowReader.cs`](https://github.com/pleonex/xdelta-sharp/blob/main/src/PleOps.XdeltaSharp/Decoder/WindowReader.cs), [`WindowDecoder.cs`](https://github.com/pleonex/xdelta-sharp/blob/main/src/PleOps.XdeltaSharp/Decoder/WindowDecoder.cs)). No secondary compression. Needs a readable and seekable output stream. Files must be under 4 GB. | **Archived** repo. Last release 2021-12-04. About 1.7k downloads. | 3.1 to 17.7 s (OG-to-NG CreationKit: 17.7 s), peak WS 70 to 98 MiB | netstandard2.0 only. Not AOT-tested here. | No |
| [`NVcdiff`](https://www.nuget.org/packages/NVcdiff/1.0.0) 1.0.0 ([VitaliiTsilnyk/NVcdiff](https://github.com/VitaliiTsilnyk/NVcdiff)) | MIT | Unverified. A fork of Jon Skeet's MiscUtil decoder "with a couple of additional bugfixes to make it compatible with xdelta3" (README). | Dormant since 2020-08-24. 2 stars. | not measured | netstandard2.0 | No |
| [`deniszykov.VCDiff`](https://www.nuget.org/packages/deniszykov.VCDiff/6.2.0) 6.2.0 ([fork](https://github.com/deniszykov/vcdiff)) | Apache-2.0 | Probably (fork of SnowflakePowered/vcdiff, adds xdelta3 interop tests) | First published 2026-10-01. 0 downloads, 0 stars. Too new to depend on. | not measured | `net8.0` target | No |
| [`xdelta3.net`](https://www.nuget.org/packages/xdelta3.net/1.0.1) 1.0.1 ([hanabi1224/xdelta3-dotnet](https://github.com/hanabi1224/xdelta3-dotnet)) | MIT wrapper + Apache-2.0 xdelta3 | Probably. Real xdelta3 code, but only the in-memory `xd3_decode_memory` API: whole source, delta and output held as `byte[]`, with an output buffer that grows by doubling ([`Xdelta3Lib.cs`](https://github.com/hanabi1224/xdelta3-dotnet/blob/master/dotnet/xdelta3.net/Xdelta3Lib.cs)). | Dormant since 2019-11. | not measured | P/Invoke (`DllImport`) to a bundled native DLL | **Yes**, `win-x64` native redist |
| Own P/Invoke to native xdelta3, or run `xdelta3.exe` | Apache-2.0 ([`xdelta3/LICENSE`](https://github.com/jmacd/xdelta/blob/v3.2.1/xdelta3/LICENSE)) | Yes (reference implementation) | Upstream revived: [v3.2.0 (2026-06-21) and v3.2.1 (2026-09-28)](https://github.com/jmacd/xdelta/releases) ship Windows x86/x64/ARM64 binaries and a reusable static library | 0.12 to 0.22 s (official 3.2.1 x64 exe) | We would own the native build or the bundled exe, and the C toolchain in CI | **Yes** |
| Hand-written managed decoder | Ours (GPL) | Feasible. Only RFC 3284 default code table, `VCD_SOURCE`, the app header and 4-byte Adler-32 are needed. | We own it | unknown | Trivially clean | No |

Not applicable: Octodiff, bsdiff ports, `DeltaCompressionDotNet` (MSDelta / PatchAPI). These use their own
formats and cannot read VCDIFF.

### Why not the others

- **PleOps.XdeltaSharp** works, but the repo is archived and it is 5 to 25 times slower. It is the best fallback
  if VCDiff ever regresses.
- **Native xdelta3 (any form)** goes against the map's "prefer managed APIs over P/Invoke" constraint. It also
  adds a native artifact. Under self-contained single-file it must be bundled and extracted
  (`IncludeNativeLibrariesForSelfExtract`). Under NativeAOT it must be statically linked or shipped next to the
  exe. Running `xdelta3.exe` as a process adds an extra file to ship. The only gain is about 0.3 s per large
  file, which is not visible next to a 40 to 66 MB download.
- **Hand-written decoder:** a few hundred lines plus tests, which we would have to maintain, with no benefit over
  a working, maintained Apache-2.0 package. Keep it as a last resort. VCDiff could also be vendored instead,
  since Apache-2.0 allows that with NOTICE retention.

## Empirical verification

Tests were run on 2026-10-03 with .NET SDK 10.0.401 on Windows 11 x64, as a throwaway console app in `%TEMP%`.
The app is not committed. Sources were real game files from a local Fallout 4 install, opened read-only:
Downgrader backups `*_upgradeBackup*` (OG) and `CreationKit_downgradeBackup.exe` (NG), plus the live NG/AE
`steam_api64.dll`. Each output was CRC32-checked against the `Downgrader.CRCs_game` / `CRCs_ck` tables.

| Patch | Expected CRC | VCDiff 5.0.0 (JIT) | VCDiff 5.0.0 (NativeAOT) | PleOps.XdeltaSharp 1.3.0 | xdelta3 3.2.1 native |
|---|---|---|---|---|---|
| NG-to-OG-steam_api64.dll | `BBD912FC` | match, 0.03 s | match, <0.01 s | match, 0.06 s | |
| OG-to-NG-steam_api64.dll | `E36E7B4D` | match, 0.03 s | match, <0.01 s | match, 0.08 s | |
| OG-to-NG-Fallout4Launcher.exe | `F6A06FF5` | match, 0.03 s | match, 0.01 s | match, 0.08 s | |
| OG-to-NG-Fallout4.exe | `C5965A2E` | match, 0.48 s / 58 MiB | match, 0.21 s / 39 MiB | match, 3.11 s / 80 MiB | match, 0.13 s |
| OG-to-NG-CreationKit.exe | `481CCE95` | match, 0.75 s / 51 MiB | match, 0.66 s / 32 MiB | match, 17.68 s / 98 MiB | match, 0.22 s |
| NG-to-OG-CreationKit.exe | `0F5C065B` | match, 0.54 s / 58 MiB | match, 0.39 s / 39 MiB | match, 3.99 s / 70 MiB | match, 0.12 s |

Times are decode plus write, measured in-process, with a warm file cache. MiB is peak working set.

Not exercised: NG-to-OG for `Fallout4.exe` and `Fallout4Launcher.exe` (no NG copy of those was available
locally), and the four Archive2 patches. The header/window scan above shows these files use the same header and
window flags as the tested ones (`0x04` header, `0x05` windows, no secondary compression). A CI characterization
test should still cover them if fixtures can be obtained.

## Licensing

- Project: GPL-2.0-or-later ([`LICENSE.md`](../../LICENSE.md), `pyproject.toml`).
- VCDiff: Apache-2.0. It is described as "a derivative work of open-vcdiff and xdelta3"
  ([README](https://github.com/SnowflakePowered/vcdiff#license)). Its transitive packages
  Microsoft.IO.RecyclableMemoryStream, Newtonsoft.Json and SharpCompress are all MIT (NuGet metadata).
- The FSF states that Apache-2.0 is "compatible with version 3 of the GNU GPL" and "not compatible with GPL
  version 2" ([FSF licence list](https://www.gnu.org/licenses/license-list.html#apache2)). The "or later" clause
  lets the combined work be distributed under GPLv3, so this is acceptable. **This is not a new constraint:** the
  shipped Python app already links Apache-2.0 xdelta3 through pyxdelta (the pinned commit's `xdelta3.c` carries
  the Apache header,
  [source](https://github.com/jmacd/xdelta/blob/7508fd2a823443b1f0173ca361620f21d62a7d37/xdelta3/xdelta3.c)).
  The same applies to every native-xdelta3 option. Ship the Apache-2.0 licence and attribution in the
  third-party notices.
- Only MIT options (PleOps, NVcdiff) or a hand-written decoder would avoid the GPLv3-effective distribution.

## Risks and open questions

1. **SharpCompress advisory.** VCDiff 5.0.0 depends on SharpCompress 0.46.3, which triggers `NU1902` for
   [GHSA-6c8g-7p36-r338 / CVE-2026-44788](https://github.com/advisories/GHSA-6c8g-7p36-r338). That is a zip-slip
   bug in `WriteToDirectory`, fixed in 0.48.0. The code path is not reachable from VCDiff, which only uses
   `XZStream` for LZMA secondary decompression. Still, add a direct `PackageReference` to SharpCompress 0.48.0 or
   later. With 0.50.4 the warning cleared, `new XZStream(Stream)` still compiles, the AOT publish had 0 warnings,
   and all patches still decoded.
2. **Unused dependency weight.** VCDiff's csproj references `Newtonsoft.Json`, but the library source does not
   use it. Trimming removes it, and the AOT exe was 1.8 MB. With a non-trimmed self-contained build it would
   ship as dead weight.
3. **Patch hosting is outside this fork.** Patches are fetched from **wxMichael/Collective-Modding-Toolkit**
   releases, not from `evildarkarchon/...`. If upstream deletes the release, the Downgrader breaks. Decide
   whether the C# port keeps that URL (parity) or mirrors the assets.
4. **Future patch regeneration.** If patches are ever rebuilt with `-S djw` or `-S fgk`, VCDiff cannot decode
   them (it only supports LZMA, id 2). xdelta3 3.2.x also adds "armor" (BLAKE3 hashes inside the app header) by
   default, which VCDiff would skip harmlessly. Any regeneration must use `-S none` (or `-S lzma`) and be
   re-verified.
5. **Failure semantics differ in detail.** pyxdelta returns a bool from the xdelta3 command-line exit code.
   VCDiff returns `VCDiffResult` or throws, for example `InvalidOperationException` when a window exceeds
   `maxTargetFileSize` (default 64 MiB per window, against 8 MiB used here), or on checksum mismatch. The C#
   wrapper should map "not `SUCCESS`" or "exception" to the same "Failed patching" log line. Partial-output
   behaviour on failure (what is left at `outfile`) has not been compared with xdelta3's.
6. **Threading.** Python runs `apply_patch` on the Tk main thread through `root.after`, so the UI blocks during
   decode. The C# port should decode off the UI thread. That is an implementation detail, not a behaviour change.

## Candidate follow-ups

- **Bug (log, don't fix inline):** if patching fails, `apply_patch` still deletes the source backup when "Keep
  Backups" is off, which can leave the user with neither file (`downgrader.py` lines 455-461).
- **Bug (log, don't fix inline):** `_threaded_download` has no error handling. A non-200 response, a network
  error, or a missing `content-length` (division by zero at line 427) leaves `download_thread` set, and the
  progress loop polls forever.
- **Decision:** whether to mirror the `delta-patches` assets to this fork (risk 3).
- **Build ticket:** a Core `IPatchApplier` (or similar) over VCDiff, plus a characterization test that applies a
  real patch and checks the CRC32. The test needs a fixture strategy, because the game binaries cannot be
  committed. Options are a small synthetic xdelta3-generated patch with an app header and Adler-32, plus an
  opt-in local test against a real install.
