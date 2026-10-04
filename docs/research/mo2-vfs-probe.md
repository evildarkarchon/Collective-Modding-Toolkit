# .NET file and process APIs under MO2's VFS

Findings for [Check how .NET file and process APIs behave under MO2's VFS](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/35), part of [Port CMT to C# / Avalonia](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/2). The probe source is in `spikes/mo2-vfs-probe/` on this branch.

## Setup

| | |
|---|---|
| Windows | 11, build 26300 (≥ 26100, so the reference's 24H2 probes are active) |
| Mod Organizer | 2.5.2 (revision `9c130cbf`), usvfs 0.5.6.1, instance install (not portable) |
| Probe | .NET 10 NativeAOT `win-x64`, as packaging specifies ([ADR-0002](../adr/0002-nativeaot-in-upstream-archive-layout.md)). Win10 `supportedOS` manifest, `asInvoker`. A `requireAdministrator` twin covers the elevated-child case. |
| Profile | `CMT-Probe`: a copy of the main profile (217 enabled mods) with profile-specific INIs on. A marker comment was appended to each profile INI so it differs from the global copy. |

No older Windows or newer MO2 was available for contrast. This is the worst-case pairing the reference warns about (OVW-5: MO2 ≤ 2.5.2 on 24H2+).

### Runs

| Run | How | Result |
|---|---|---|
| Baseline | Probe run outside MO2 | Ground truth for the real `Data`. Built `targets.json`, dropping candidates already present in the real `Data`. |
| A | MO2 normal → `cmt-vfs-probe.exe` (CET off) | Completed, twice |
| B | MO2 normal → `cmt-vfs-probe-admin.exe` (`requireAdministrator`) | usvfs: `failed to spawn`. MO2 2.5.2 offers to restart itself elevated, which turns B into C. |
| C | MO2 elevated → `cmt-vfs-probe.exe` | Completed |
| CET | MO2 normal → `cmt-vfs-probe-cet.exe` (default `/CETCOMPAT`) | **Crash before `Main`**, twice (see below) |

## 1. Plain .NET APIs see the VFS

Every target probed outside MO2 (baseline) and in runs A and C. Runs A and C gave identical results, so they share a column.

| Target | Baseline | Under MO2 (A, C) | `GetFileInformationByName` under MO2 |
|---|---|---|---|
| Real file (`Fallout4.exe`, `Data\Fallout4.esm`) | `File.Exists` ✔, listed ✔ | `File.Exists` ✔, listed ✔ | attrs `0x20` ✔ |
| Real dir (`Data`) | `Directory.Exists` ✔ | `Directory.Exists` ✔ | attrs `0x201` ✔ |
| Missing file / dir / file in missing dir | all ✘ | all ✘ | error 2 / 2 / 3 |
| 6 VFS-only `Data\*.ba2` | ✘ | **`File.Exists` ✔, open ✔ (reads bytes), listed in `Data` ✔** | **error 2 ✘** |
| 4 VFS-only `Data\F4SE\Plugins\*.dll` (in a real folder) | ✘ | **`File.Exists` ✔, open ✔, listed ✔** | **error 2 ✘** |
| 4 VFS-only folders at depth 2 (e.g. `Data\textures\<mod>`) | ✘ | **`Directory.Exists` ✔, listed ✔** | **error 2 ✘** |

There were no VFS-only folder candidates at the top level, because every top-level mod folder name already exists in the real `Data`. The depth-2 folders stand in for them.

- `File.Exists`, `Directory.Exists`, `FileInfo`/`DirectoryInfo.Exists`, `File.GetAttributes`, `FileStream` open+read and directory enumeration all see VFS-only entries. usvfs 0.5.6.1 hooks `GetFileAttributesExW`, `NtQueryFullAttributesFile`, `NtQueryAttributesFile`, `NtQueryDirectoryFile(Ex)`, `NtOpenFile` and `NtCreateFile`, which is every path the BCL uses.
- **`GetFileInformationByName` misses every VFS-only entry.** CPython 3.12+ calls it for `os.stat` on 24H2+, and usvfs 0.5.6.1 doesn't hook it. This reproduces the bug that made the reference add its open/iterdir probes. The cause is Python-specific: .NET never calls this API.
- Enumeration merges the VFS. `Data` top-level entries go from 2209 to 2313, and a recursive walk finds 61,308 files instead of 58,960. Counts by pattern also rise (`*.ba2` 908 → 977, `*.esp` 663 → 693, `F4SE\Plugins\*.dll` 84 → 90). A full recursive walk of `Data` took about 0.9 s under MO2, against 0.2 s outside it.

## 2. Profile files are served

| File (reference path) | Baseline hash | Under MO2 (A, C) |
|---|---|---|
| `Documents\My Games\Fallout4\Fallout4.ini` | global copy | **the marked `CMT-Probe` copy** |
| `…\Fallout4Prefs.ini` | global copy | **the marked `CMT-Probe` copy** |
| `…\Fallout4Custom.ini` | global copy | **the marked `CMT-Probe` copy** |
| `%LOCALAPPDATA%\Fallout4\plugins.txt` | global (954 `*` lines) | **the profile's copy** (520 `*` lines) |

`File.ReadAllBytes` at the reference's paths returns the profile-local INIs and the profile's `plugins.txt` (INI-2, OVW-M4). `Environment.GetFolderPath(MyDocuments / LocalApplicationData)` resolved to the same folders in every run.

## 3. Parent-process walk (MM-1, T-5)

| | MO2 normal (A) | MO2 elevated (C) |
|---|---|---|
| Toolhelp32 `szExeFile` at depth 1 | `ModOrganizer.exe` | `ModOrganizer.exe` |
| `Process.ProcessName` | `ModOrganizer` (no `.exe`) | `ModOrganizer` |
| `Process.MainModule.FileName` | ✔ | ✔ (same integrity level) |
| `QueryFullProcessImageNameW` (limited access) | ✔ | ✔ |
| Parent token elevated | false | true |
| `FileVersionInfo` | `2.5.2.0` | `2.5.2.0` |

Toolhelp32's `szExeFile` matches MM-1's exact `ModOrganizer.exe` comparison with no handle needed. `QueryFullProcessImageNameW` gives the path. Both work at either elevation, so the port can skip `Process.ProcessName`'s missing `.exe` suffix and `MainModule`'s elevation fragility altogether.

The elevated-child-of-a-normal-MO2 case can't happen. MO2 2.5.2 refuses to spawn a `requireAdministrator` child and restarts itself elevated instead, and the toolkit is `asInvoker` anyway.

## 4. Reference probe vs. .NET: one divergence

Under usvfs, **enumerating a file succeeds and returns no entries**. That held for every real and VFS-only file. Outside MO2 the same .NET call throws `IOException 0x80070057`. So an emulation of `utils.is_dir` (24H2 branch: true unless `iterdir` raises `NotADirectoryError`/`FileNotFoundError`) built on .NET enumeration says **files are directories** under MO2.

Python's `iterdir` goes through `FindFirstFileExW`, which usvfs hooks separately, so the reference's actual behaviour is unconfirmed. It's filed as the suspected reference bug [Under MO2 on Windows 24H2+, `is_dir` may report files as directories](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/43). .NET's own `Directory.Exists` was correct (false) on every file. The port follows .NET under the existing named exemption.

Outside MO2, the emulated reference probe and .NET agreed on all 21 targets.

## 5. CET: a CET-compatible exe crashes before `Main` under MO2 2.5.2

NativeAOT links `/CETCOMPAT` by default on x64 (`Microsoft.NETCore.Native.Windows.targets`: "Opt into CETCOMPAT by default"). The first run-A probe had that flag, and it died with `0xC0000005` about 2 s after launch, before `Main`. The usvfs log shows the injection and `inithooks … successful`. The minidump shows the main thread inside usvfs's injection stub in allocated memory (`pop r8…r15; ret`, faulting on the `ret`), returning to `ntdll!RtlUserThreadStart`. That `ret` was never paired with a `call`, so the CET shadow stack rejects it.

| Build | Under MO2 2.5.2 |
|---|---|
| `/CETCOMPAT` (NativeAOT default) | crashes before `Main`: PIDs 43296 and 49684, same fault offset `…00b7` |
| `<CETCompat>false</CETCompat>` | runs normally (A ×2, C) |

This only bites on CPUs with CET shadow-stack support (roughly Intel 11th gen+ and AMD Zen 3+), which is the machine tested here. The reference never hit it because PyInstaller exes aren't CET-compatible. Whether a newer usvfs fixes the stub is unverified.

**Consequence for the port:** the shipped exe must be published with `<CETCompat>false</CETCompat>` (`/CETCOMPAT:NO`). Otherwise the C# toolkit can't start from MO2 2.5.2, which is the main way the reference is launched.

## Answer

- **Plain .NET APIs see MO2's VFS:** VFS-only files and folders, profile-local INIs and the profile `plugins.txt` all come through. No port-side probe is needed, so the FileProbe parity exemption stands confirmed.
- **The parent walk works at both elevations** via Toolhelp32 `szExeFile` and `QueryFullProcessImageNameW`.
- **Two new facts:**
  1. The NativeAOT exe must opt out of CET.
  2. The reference's 24H2 `is_dir` is suspected of calling files directories under MO2.
