# .NET 10 equivalents for the Python app's remaining dependencies

Research for issue #4 (map: #2). Scope: every third-party Python dependency in `pyproject.toml` except `pyxdelta` (separate ticket), plus the stdlib modules whose .NET mapping isn't obvious. Each use site was found by grepping `src/`, not guessed. Locked versions from `uv.lock`: chardet 5.2.0, packaging 25.0, pillow 12.0.0, psutil 7.1.3, pywin32 311, pywin32-ctypes 0.2.3, requests 2.32.5, tkinter-tooltip 3.1.2.

Standing constraints from the map: Windows-only; prefer managed .NET APIs over P/Invoke; port behaviour as-is; licences must be compatible with GPL-2.0-or-later; the app may ship as NativeAOT or as self-contained single-file, so trimming and AOT compatibility matter.

**Evidence beyond the docs:** I ran a throwaway spike (deleted afterwards, not committed) on Windows 11 build 26300 with .NET SDK 10.0.401. It ran the candidate .NET APIs side by side with the real Python libraries (psutil, pywin32, packaging, run through `uv`). The same spike was also **NativeAOT-published** (`PublishAot=true`, win-x64). That spike covered `FileVersionInfo`, `Registry`, `Environment.GetFolderPath`, `GC.GetGCMemoryInfo`, `NativeLibrary.TryGetExport`, `PEReader`, `JsonDocument`, `HttpClient`, `System.IO.Hashing.Crc32` and strict UTF-8 decoding. It compiled with **zero trim/AOT warnings**, and the native binary produced the same output as the JIT build. Results marked "(spike)" below come from that run.

## Summary

| Python dep | Use sites (`src/`) | .NET replacement | Package? | AOT/trim-safe? | Parity risk |
|---|---|---|---|---|---|
| `chardet` | `utils.py:151` `read_text_encoded()`, which is **never called** (dead code) | None. Drop it. | No | n/a | None. INIs are actually read as strict UTF-8 (see stdlib rows). |
| `psutil.virtual_memory()` | `helpers.py:84` `PCInfo._get_ram()` | `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes` | No (BCL) | Yes (spike) | Low. Matched psutil byte-for-byte (spike). Diverges only under a job-object limit or `GCHeapHardLimit`. |
| `psutil.Process` (`.parent()`, `.name()`, `.exe()`, `.oneshot()`) | `utils.py:177-196` `find_mod_manager()`, which walks up to 8 parents looking for `ModOrganizer.exe`/`Vortex.exe` | `System.Diagnostics.Process` for name/path/start time. **Parent PID has no managed API**, so it needs a `[LibraryImport]` P/Invoke (`CreateToolhelp32Snapshot` or `NtQueryInformationProcess`). | No | Yes, with `LibraryImport` | **Medium.** psutil reads the exe path without opening a process handle. `Process.MainModule` needs module-read access, so it can fail across an elevation boundary. `ProcessName` drops the `.exe`. |
| `pywin32` `win32api.GetFileVersionInfo` + `HIWORD`/`LOWORD` | `utils.py:212-224` `get_file_version()`. Called from `utils.py:192` (mod manager version) and `tabs/_overview.py:635` (game binaries) | `FileVersionInfo.GetVersionInfo(p).FileMajorPart/FileMinorPart/FileBuildPart/FilePrivatePart` | No (BCL) | Yes (spike) | **Medium.** (1) The exe **must ship a Win10 `supportedOS` manifest**, or Windows version-lies on OS binaries (spike: `6.2.x` vs pywin32's `10.0.x`). (2) No version resource: pywin32 raises (Python returns `None`) while .NET returns `0.0.0.0` without throwing. (3) Use the `*Part` properties, never the `FileVersion` string. |
| `pywin32-ctypes` | **No import anywhere in `src/`** | None | No | n/a | None |
| `ctypes` `shell32.SHGetFolderPathW` + `enums.CSIDL` | `utils.py:163-170` `get_environment_path()`. Callers: `game_info.py:83` (Documents) and `tabs/_overview.py:909` (LocalAppData). `CSIDL.Desktop`/`AppData` are defined but unused. | `Environment.GetFolderPath(SpecialFolder.MyDocuments / LocalApplicationData)`. `SpecialFolder` values *are* CSIDL values (0/5/26/28). | No (BCL) | Yes (spike) | Low. .NET returns `""` for a missing folder; map that to the same `FileNotFoundException`. |
| `ctypes` `gdi32.AddFontResourceExW` | `utils.py:155-160`, called from `main.py:45` (CascadiaMono.ttf) | Avalonia embedded font (`AvaloniaResource` + `avares://…#Cascadia Mono`) | No (Avalonia) | Yes | None |
| `ctypes` `user32.GetParent` + `dwmapi.DwmSetWindowAttribute(19/20)` | `utils.py:318-322` `set_titlebar_style()`. Called from `set_theme()` and `modal_window.py:60` | Avalonia theme variant / window chrome. Fallback: `LibraryImport` of `DwmSetWindowAttribute` on `TryGetPlatformHandle()`. | No | Yes | Low. Belongs to the UI ticket. |
| `ctypes` `windll.ntdll` `wine_get_version` probe | `helpers.py:69` `PCInfo.using_wine` | `NativeLibrary.TryGetExport(NativeLibrary.Load("ntdll.dll"), "wine_get_version", out _)` | No (BCL) | Yes (spike) | None |
| `ctypes.WinDLL(..., DONT_RESOLVE_DLL_REFERENCES)` + `ctypes.Structure` (`F4SEPluginVersionData`) | `utils.py:62-75, 242-273` `parse_dll()`, called from `tabs/_f4se.py:65` for each F4SE plugin | `System.Reflection.PortableExecutable.PEReader`: parse the export directory, map the `F4SEPlugin_Version` RVA to file bytes, then read the struct with `BinaryPrimitives` or `MemoryMarshal` | No (BCL) | Yes (spike) | Low/Medium. Fully managed and never maps or executes the plugin. Edge cases: forwarded exports, and non-x64 DLLs (Python raises; PEReader parses them). |
| `winreg` (stdlib) | `utils.py:283-294` `get_registry_value()`. Callers: `game_info.py:191` (MO2 `CurrentInstance`, HKCU) and `game_info.py:221-228` (Bethesda/GOG install path, HKLM WOW6432Node). Also `helpers.py:93-121` (CPU name, GPU name, VRAM). | `Microsoft.Win32.Registry` / `RegistryKey` | No (BCL) | Yes (spike) | Low. Keep the `REG_SZ`-only / `REG_QWORD`-only checks via `GetValueKind()`, because `GetValue()` silently expands `REG_EXPAND_SZ`. |
| `requests` | `utils.py:357-417` update check (Nexus HTML scrape, GitHub releases API). `downgrader.py:420` streams the delta-patch download. | `HttpClient` (+ `HttpCompletionOption.ResponseHeadersRead`, `StreamReader.ReadLineAsync`) | No (BCL) | Yes (spike) | **Medium.** `HttpClient` sends **no User-Agent**, and GitHub's API rejects UA-less requests. The timeouts also mean different things: requests uses per-socket-op timeouts, `HttpClient.Timeout` is a total. |
| `packaging.version.Version` | `utils.py:193, 381, 409`, `tabs/_overview.py:119` (`manager.version <= Version("2.5.2")`), `mod_manager_info.py` (type only) | **Small hand-written PEP 440-subset comparer.** `System.Version` is *not* a drop-in. | No | Yes | **High if `System.Version` is used.** It rejects `"v0.6.2"` and `"0.6.2-dev"` (the app's own `APP_VERSION`), and it ranks `1.2 < 1.2.0` where PEP 440 has them equal (spike). |
| `tkinter-tooltip` | `ToolTip(...)` in `cm_checker.py`, `downgrader.py`, `tabs/_overview.py`, `tabs/_scanner.py`, `tabs/_settings.py`, `tabs/_tools.py` (~30 sites) | Avalonia `ToolTip.Tip` attached property | No (Avalonia) | Yes | Cosmetic. tktooltip defaults to 0 s delay, follow-the-cursor, +10/+10 offset; Avalonia defaults to 400 ms, pointer placement, 0/20 offset. |
| `Pillow` / PIL | **No import anywhere in `src/`.** Images load through Tk's built-in `PhotoImage` (PNG). | Avalonia `Bitmap` / `Image` | No | Yes | None. Pillow is a dead dependency. |
| stdlib text decoding of INI/TXT files | `game_info.py:91` (Fallout4*.ini), `mod_manager_info.py:132` (ModOrganizer.ini), `tabs/_overview.py:892,911` (CCC, plugins.txt), `tabs/_scanner.py:327` (modlist.txt), `app_settings.py:33,84` | `File.ReadAllBytes` + `new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(...)` (no BOM sniffing), plus a Python-compatible line splitter | No | Yes | **Medium.** .NET's defaults strip the BOM and swallow invalid bytes. Python keeps U+FEFF and raises. (spike) |
| stdlib INI parsing (no `configparser`!) | Hand-rolled `split("=", 1)` loops in `game_info.py:84-102` and `mod_manager_info.py:131-162` | Port the loops verbatim. Do **not** use `Microsoft.Extensions.Configuration.Ini` or any other INI library. | No | Yes | High if a library is substituted: it trims, treats `; # /` as comments, and throws on duplicate keys. |
| `csv.reader(..., escapechar="\\", doublequote=False, skipinitialspace=True)` | `mod_manager_info.py:175` (MO2 `skip_*` lists) | Small hand-written splitter. `TextFieldParser` has no escape-char support. | No | Yes | Low |
| `zlib.crc32` | `utils.py:227-239` `get_crc32()`, used for game/CK binary identification and the Downgrader | `System.IO.Hashing.Crc32` (`GetCurrentHashAsUInt32()`, formatted `X8`) | **Yes**: `System.IO.Hashing` (Microsoft, MIT, out-of-band) | Yes (spike) | Low. Don't use `GetCurrentHash()` bytes, which are little-endian (spike). A 20-line table CRC avoids the package entirely. |
| `struct.unpack("<f"/"<I")` + `round()` + `str(float)` | `tabs/_overview.py:979-994`, `utils.py:427` | `BinaryPrimitives.ReadSingleLittleEndian` / `ReadUInt32LittleEndian` | No | Yes | **Medium.** `str(hedr)` is matched against strings like `"1.0"`. C# `1.0.ToString()` gives `"1"`, so Python float repr has to be emulated. |
| `json` | `app_settings.py:84,133` | `System.Text.Json` with a source-generated `JsonSerializerContext`. `JsonDocument` for the GitHub reply. | No | Yes, with source gen | Low |
| `webbrowser.open`, `os.startfile` | `cm_checker.py:150,178`, `tabs/_about.py`, `tabs/_scanner.py:792,813`, `tabs/_tools.py:55`, `tabs/_overview.py:170` | `Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })` | No | Yes | Low. `UseShellExecute` defaults to **false** on .NET. |
| `platform.release()` + `sys.getwindowsversion().build` | `helpers.py:77-79` (OS label), `utils.py:57` / `mod_manager_info.py:31` (`build >= 26100`) | `Environment.OSVersion.Version.Build` | No | Yes | **Medium.** Python returns `"11"` for build >= 22000; .NET reports major `10`. `_overview.py:119` compares against the literal `"Windows 11 24H2"`. |

**Headline answers**

- **Forced NuGet packages:** at most one, `System.IO.Hashing` (Microsoft, MIT, AOT-clean), and only if a hand-rolled CRC32 is not preferred. Avalonia, the UI framework, is assumed and out of scope here.
- **Forced P/Invoke:** only one is unavoidable, the **parent-process lookup** in `find_mod_manager()`. Two more are optional or conditional: `QueryFullProcessImageNameW`, if exact psutil parity across elevation boundaries is wanted, and `DwmSetWindowAttribute`, if Avalonia doesn't already darken the title bar. All three are trivially AOT-safe through `[LibraryImport]`.
- **Native binaries:** none come from this set of dependencies. The only native payloads are Avalonia's own (Skia/HarfBuzz) and whatever replaces pyxdelta (separate ticket).
- **Not AOT/trim-safe:** none of the recommended replacements. `System.Management` (WMI) would be the one to avoid if someone reached for it to get the parent PID.

## Per-dependency detail

### chardet: dead code

- **Use:** `utils.py:149-152` defines `read_text_encoded()`, which runs `chardet.detect(file_bytes)["encoding"] or "utf-8"`. Grepping `src/` for `read_text_encoded` finds only the definition; nothing calls it.
- **Actual INI behaviour:** every INI/TXT read uses `read_text(encoding="utf-8")` (strict). See [Text decoding](#text-decoding-of-inis-and-txt-files).
- **Recommendation:** don't port it. No charset detector is needed, so there is no detector-versus-chardet disagreement to worry about. If detection is ever reintroduced, treat it as a new feature: chardet itself is LGPL-2.1+ ([PyPI](https://pypi.org/project/chardet/5.2.0/)), and any .NET detector would need its own licence and parity check.

### psutil

#### `virtual_memory().total` (`helpers.py:84`)

- **Use:** `round(mem.total / 1024**3)` produces the GiB figure on the Overview "specs" line.
- **What psutil does (7.1.3):** `virtual_memory()` reads `cext.virtual_mem()["totphys"]` ([_pswindows.py@release-7.1.3](https://github.com/giampaolo/psutil/blob/release-7.1.3/psutil/_pswindows.py)). The C side calls `GetPerformanceInfo` and computes `totalPhys = perfInfo.PhysicalTotal * pageSize` ([mem.c@release-7.1.3](https://github.com/giampaolo/psutil/blob/release-7.1.3/psutil/arch/windows/mem.c)).
- **Replacement:** `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`. On Windows the GC derives this from `GlobalMemoryStatusEx().ullTotalPhys`, unless a job-object memory limit is lower ([gcenv.windows.cpp](https://github.com/dotnet/runtime/blob/main/src/coreclr/gc/windows/gcenv.windows.cpp)). The documented exceptions are `GCHeapHardLimit` and containers ([GCMemoryInfo.TotalAvailableMemoryBytes](https://learn.microsoft.com/en-us/dotnet/api/system.gcmemoryinfo.totalavailablememorybytes?view=net-10.0)).
- **Spike:** with zero GCs having run, .NET returned `68293742592`, exactly psutil's `68293742592`. Both rounded to 64 GiB, and the AOT build gave the same value.
- **Do not use** `Microsoft.VisualBasic.Devices.ComputerInfo.TotalPhysicalMemory`. It lives in `Microsoft.VisualBasic.Forms` and the `windowsdesktop` (WinForms) framework ([docs](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.devices.computerinfo.totalphysicalmemory?view=net-10.0)).
- **Exact-parity fallback:** a `[LibraryImport]` of `GetPerformanceInfo` or `GlobalMemoryStatusEx`. I don't think it's needed, given the rounding to whole GiB.

#### `Process` tree walk (`utils.py:177-196`)

- **Use:** start at `os.getppid()`, walk up to 8 ancestors, and stop at the first whose `name()` is `ModOrganizer.exe` or `Vortex.exe` (exact, case-sensitive). Then take `exe()` for the path and pass it to `get_file_version()`.
- **What psutil does (7.1.3):**
  - `name()` is `basename(exe())`.
  - `exe()` comes from `NtQuerySystemInformation(SystemProcessIdInformation)`, which needs **no process handle**.
  - `ppid()` comes from a `CreateToolhelp32Snapshot` map ([proc.c@release-7.1.3](https://github.com/giampaolo/psutil/blob/release-7.1.3/psutil/arch/windows/proc.c), [_pswindows.py](https://github.com/giampaolo/psutil/blob/release-7.1.3/psutil/_pswindows.py)).
  - `parent()` returns the parent only if `parent.create_time() <= child.create_time()`, to guard against PID reuse ([__init__.py@release-7.1.3](https://github.com/giampaolo/psutil/blob/release-7.1.3/psutil/__init__.py)).
- **.NET:** `System.Diagnostics.Process` has **no parent-process member** (checked against the [Process class member list](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process?view=net-10.0)). So the parent PID needs a P/Invoke:
  - Option A, `CreateToolhelp32Snapshot` + `Process32FirstW/NextW`: the same source psutil uses, and documented API.
  - Option B, `NtQueryInformationProcess(ProcessBasicInformation)`: needs a handle and is semi-documented.
  - Declare either with `[LibraryImport]`. Source-generated marshalling is what makes P/Invoke AOT/trim-compatible ([P/Invoke source generation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke-source-generation)).
  - Avoid `System.Management` (WMI `Win32_Process.ParentProcessId`). It's a NuGet package on built-in COM interop, and NativeAOT lists *"Windows: No built-in COM"* as a limitation ([Native AOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment)).
- **Name/path parity:**
  - `Process.ProcessName` comes from the system process snapshot (no handle needed) but **drops the `.exe` extension**. Either compare against `ModOrganizer`/`Vortex`, or compare `Path.GetFileName(exePath)`.
  - `Process.MainModule.FileName` enumerates modules. It throws `Win32Exception` in some cross-bitness and access-denied cases ([MainModule](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.mainmodule?view=net-10.0)). This matters when MO2 runs elevated and the toolkit doesn't, which is possible when MO2 launches it as an executable with different elevation.
  - For full parity, add `QueryFullProcessImageNameW` with `PROCESS_QUERY_LIMITED_INFORMATION`. It's the documented, minimal-access way to get the image path; psutil's handle-free NT call is undocumented.
  - The PID-reuse guard maps to `Process.StartTime`, which also needs a limited-access handle.

### pywin32 `win32api.GetFileVersionInfo` + `HIWORD`/`LOWORD`

- **Use:** `get_file_version()` returns `(HIWORD(MS), LOWORD(MS), HIWORD(LS), LOWORD(LS))` from `VS_FIXEDFILEINFO`, or `None` on *any* exception (bare `except`).
  - In `tabs/_overview.py:635-645`, a `None` result makes `VersionString` fall back to the CRC32 hash, and install-type lookup goes by hash.
  - In `find_mod_manager()`, `None` becomes `Version("0.0.0")`.
- **Replacement:** `FileVersionInfo.GetVersionInfo(path)` with `FileMajorPart`/`FileMinorPart`/`FileBuildPart`/`FilePrivatePart`. The runtime computes these exactly as `HIWORD(dwFileVersionMS)`, `LOWORD(dwFileVersionMS)`, `HIWORD(dwFileVersionLS)`, `LOWORD(dwFileVersionLS)` ([FileVersionInfo.Windows.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/System.Diagnostics.FileVersionInfo/src/System/Diagnostics/FileVersionInfo.Windows.cs)). It calls `GetFileVersionInfoSizeEx(FILE_VER_GET_LOCALISED)` / `GetFileVersionInfoEx(FILE_VER_GET_LOCALISED | FILE_VER_GET_NEUTRAL)`.
- **Parity traps:**
  1. **App manifest / version lie (spike).**
     - Without a manifest, .NET reported `notepad.exe` as `6.2.26100.9278`; pywin32 under python.exe reported `10.0.26100.9278`.
     - With an `app.manifest` declaring the Windows 10 `supportedOS` GUID `{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`, .NET reported `10.0.26100.9278`, a match.
     - This is the same compatibility shim that makes un-manifested apps see Windows 8 (6.2) ([Targeting your application for Windows](https://learn.microsoft.com/en-us/windows/win32/sysinfo/targeting-your-application-at-windows-8-1), [GetVersionEx](https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/nf-sysinfoapi-getversionexw)).
     - It seems to affect OS binaries rather than `Fallout4.exe`, but the fix is a one-line manifest. Make it a packaging requirement.
  2. **No version resource.**
     - pywin32 raises `error 1812 ... did not contain a resource section` for `win.ini`, which Python turns into `None`.
     - .NET returns an object with all parts `0` and an empty `FileVersion`, and does not throw (spike; the source skips everything when `infoSize == 0`).
     - Port as: treat all-zero `*Part` values as "no version" (returns `null`). The only divergence is a file that genuinely carries a `0.0.0.0` fixed version, which pywin32 would report as `(0,0,0,0)`. Then `ver_to_str` gives `"0.0.0.0"` rather than falling back to the hash. That's negligible, but write it down.
     - A missing file throws `FileNotFoundException` in .NET (spike), which matches the "exception gives `None`" path if caught.
  3. **Don't use `FileVersion` (string).** It comes from the `StringFileInfo` table and can differ from the fixed info (spike: `notepad.exe` string `10.0.26100.8457` vs fixed `…9278`).

### ctypes call sites

| Python | .NET | Notes |
|---|---|---|
| `shell32.SHGetFolderPathW(None, csidl, None, 0, buf)` | `Environment.GetFolderPath(SpecialFolder.X)` | `SpecialFolder` *is* the CSIDL enum: Desktop=0, MyDocuments=5, ApplicationData=26, LocalApplicationData=28 ([SpecialFolder](https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder?view=net-10.0); confirmed by spike). With the default `SpecialFolderOption.None`, a non-existent folder returns `""` ([GetFolderPath](https://learn.microsoft.com/en-us/dotnet/api/system.environment.getfolderpath?view=net-10.0)). Python does its own `is_dir` check and raises `FileNotFoundError("Folder does not exist:\n…")`, so keep that check and message. Flag 0 = `SHGFP_TYPE_CURRENT`, the redirected path ([SHGetFolderPath](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetfolderpatha)), which is also what .NET returns. |
| `gdi32.AddFontResourceExW(path, FR_PRIVATE)` | Avalonia embedded font | Embed as `AvaloniaResource` and reference `avares://Asm/Path#Family`; no system-wide install ([Avalonia fonts](https://docs.avaloniaui.net/docs/guides/styles-and-resources/how-to-use-fonts)). |
| `user32.GetParent(winfo_id())` + `dwmapi.DwmSetWindowAttribute(hwnd, 19 and 20, TRUE)` | Avalonia theming; fallback `[LibraryImport] DwmSetWindowAttribute` on `TopLevel.TryGetPlatformHandle()` | Attribute 20 is `DWMWA_USE_IMMERSIVE_DARK_MODE`, documented from Windows 11 build 22000. 19 is the undocumented pre-20H1 value ([DWMWINDOWATTRIBUTE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute)). `GetParent` is a Tk artefact; Avalonia exposes the HWND directly. Open question for the UI ticket: does Avalonia's dark theme already darken the native title bar? |
| `hasattr(windll.ntdll, "wine_get_version")` | `NativeLibrary.TryGetExport(NativeLibrary.Load("ntdll.dll"), "wine_get_version", out _)` | Managed wrapper over `GetProcAddress` ([TryGetExport](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativelibrary.trygetexport?view=net-10.0)). Spike: `False` on real Windows; works under AOT. |
| `ctypes.WinDLL(path, winmode=DONT_RESOLVE_DLL_REFERENCES)` + `hasattr(dll, "F4SEPlugin_*")` + `cast(dll.F4SEPlugin_Version, POINTER(F4SEPluginVersionData))` | `PEReader` over a `FileStream`: walk `PEHeaders.PEHeader.ExportTableDirectory` → export name table → RVA; get bytes via `GetSectionData(rva)`; decode the 1,124-byte struct (`uint32` ×2, `char[256]` ×2, `uint32` ×2, `uint32[16]`, `uint32` ×3, `uint8[512]`) with `BinaryPrimitives` | Microsoft says *"Do not use this value [DONT_RESOLVE_DLL_REFERENCES]; it is provided only for backward compatibility"* ([LoadLibraryExW](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw)). `NativeLibrary.Load` can't pass that flag anyway, and would run `DllMain` and resolve imports. Reading the file never executes plugin code. The struct holds no pointers, so loader relocations don't change its bytes. Spike: `PEReader` read `version.dll`'s export directory under AOT. Edge cases to test: forwarded exports (`GetProcAddress` follows them), and 32-bit or ARM64 DLLs (Python's `LoadLibraryEx` fails with an `OSError`, which `_f4se.py` doesn't catch; PEReader parses them fine). Port-as-is means deciding whether to reproduce that failure. |

### winreg → `Microsoft.Win32.Registry`

- **Use sites:**
  - `get_registry_value()` returns the value only if `value_type == REG_SZ` and it's a non-empty string. Callers read HKCU `Software\Mod Organizer Team\Mod Organizer\CurrentInstance`, HKLM `SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4\Installed Path`, and HKLM `SOFTWARE\WOW6432Node\GOG.com\Games\1998527297\path`.
  - `PCInfo`: `ProcessorNameString` (REG_SZ), `HARDWARE\DEVICEMAP\VIDEO\\Device\Video0` (REG_SZ, `\Registry\Machine\` prefix stripped), `HardwareInformation.AdapterString` (REG_SZ), `HardwareInformation.qwMemorySize` (REG_QWORD).
- **Replacement:** `RegistryKey.OpenBaseKey(RegistryHive.X, RegistryView.Registry64).OpenSubKey(...)`. Check the type with `GetValueKind(name) == RegistryValueKind.String` / `QWord` before `GetValue`. Spike: every key above read correctly, with kinds `String`/`String`/`String`/`QWord`.
- **Parity notes:**
  - `GetValue` expands `REG_EXPAND_SZ` automatically unless `RegistryValueOptions.DoNotExpandEnvironmentNames` is passed ([GetValue](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registrykey.getvalue?view=net-10.0), [RegistryValueOptions](https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.registryvalueoptions?view=net-10.0)). Python rejects those values through its `REG_SZ` check, so the `GetValueKind` check is what preserves behaviour.
  - Python (64-bit) opens the 64-bit view and names `WOW6432Node` explicitly. Pin `RegistryView.Registry64` so an accidental x86 build doesn't double-redirect.
  - `winreg` raises `OSError` for a missing key; `OpenSubKey` returns `null`. Handle both as "not found".

### requests → `HttpClient`

- **Use sites:**
  1. `check_for_update_nexus()`: `GET NEXUS_LINK`, `timeout=5, stream=True`, then `iter_lines(decode_unicode=True)` until the line after `<meta property="twitter:label1" content="Version"`, then `rsplit('"', 2)[1]`.
  2. `check_for_update_github()`: `GET api.github.com/repos/wxMichael/.../releases/latest` with `Accept: application/vnd.github+json` and `X-GitHub-Api-Version: 2022-11-28`, `timeout=5`, then `json()["tag_name"]`.
  3. `Downgrader._threaded_download()`: `GET {PATCH_URL_BASE}{file}` (a GitHub release asset, so it redirects), `timeout=10, stream=True`, `iter_content(1024)` to a file in the CWD, with progress computed as `downloaded/content-length`.
- **Replacement:** one shared `HttpClient`, BCL and AOT-safe (spike). For JSON, `JsonDocument.Parse(...).RootElement.GetProperty("tag_name")` needs no serializer metadata (spike, AOT).
- **Parity traps:**
  - **User-Agent.** requests sends `python-requests/<ver>` ([requests advanced docs](https://requests.readthedocs.io/en/latest/user/advanced/)). `HttpClient` sends none (spike: `DefaultRequestHeaders.UserAgent.Count == 0`). GitHub: *"Requests with no `User-Agent` header will be rejected"* ([GitHub REST getting started](https://docs.github.com/en/rest/using-the-rest-api/getting-started-with-the-rest-api)). **Set an explicit UA**, e.g. `CM-Toolkit/<version>`, or the GitHub update check breaks. Nexus is an HTML scrape and may also treat UA-less clients differently (unverified).
  - **Timeout semantics.**
    - requests' `timeout=5` applies separately to connect and to *each read*, and is not a wall-clock limit ([requests docs, Timeouts](https://requests.readthedocs.io/en/latest/user/advanced/)).
    - `HttpClient.Timeout` (default 100 s) covers the whole call ([Timeout](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.timeout?view=net-10.0)). With `ResponseHeadersRead` it covers only the time until headers arrive, and content reads must be timed out separately ([HttpCompletionOption](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)).
    - So, for the multi-MB delta patches: `ResponseHeadersRead`, a per-read `CancellationTokenSource` reset after each chunk (10 s), and `SocketsHttpHandler.ConnectTimeout`. A plain `Timeout = 10s` would kill large downloads.
  - **Decoding of `iter_lines(decode_unicode=True)`.** requests uses the `Content-Type` charset. If the type is `text/*` and has no charset, it uses ISO-8859-1 (same docs, "Encodings"). Use `StreamReader` with the response charset and fall back to Latin-1 for `text/*`. Version strings are ASCII, so the risk is low. `iter_lines` splits like `splitlines()`; `ReadLine` splits only on CR/LF/CRLF. That doesn't matter for HTML.
  - **Quirks to port as-is, or deliberately fix** (behaviour decision for the map):
    - The Downgrader never checks the status code, so a 404 body gets written as the patch file.
    - It divides by `content-length` with a default of 0, so a missing header raises `ZeroDivisionError`.
    - `except requests.RequestException` corresponds to `HttpRequestException` plus `TaskCanceledException` (timeout).
  - requests verifies TLS against `certifi` (same docs); `HttpClient` on Windows uses the OS certificate store. That's benign.

### packaging.version.Version → hand-written PEP 440 subset

- **Inputs actually compared:**
  - `APP_VERSION = "0.6.2-dev"` (`globals.py:23`).
  - The Nexus `<meta>` version string.
  - GitHub `tag_name`, which can carry a leading `v`.
  - MO2/Vortex versions built from file-version ints (`"2.5.2"`).
  - The literal `Version("2.5.2")`.
- **PEP 440 rules in play** ([Version specifiers spec](https://packaging.python.org/en/latest/specifications/version-specifiers/)):
  - A shorter release segment is padded with zeros.
  - A leading `v` is ignored.
  - `1.2-dev` normalises to `1.2.dev0`.
  - `.devN` sorts before `aN`/`bN`/`rcN`, which sort before the release, which sorts before `.postN`.
  - Invalid strings raise `InvalidVersion`, which the update checks catch and log.
- **Why not `System.Version`** (spike):
  - `Version.TryParse("0.6.2-dev")` and `TryParse("v0.6.2")` both return **false**.
  - `new Version("1.2").CompareTo(new Version("1.2.0")) == -1`. The documented rule is that an unknown component is older than any known one ([Version.CompareTo](https://learn.microsoft.com/en-us/dotnet/api/system.version.compareto?view=net-10.0)). PEP 440 treats them as equal (Python spike: `Version('1.2') == Version('1.2.0')` is `True`).
- **Recommendation:** a ~100-line internal `Pep440Version : IComparable` covering epoch-less `N(.N)*` with optional leading `v`, `{a|b|rc}N`, `.postN`, `.devN`, separator normalisation, and zero-padded comparison. Throw on anything else to mirror `InvalidVersion`. Port test vectors from packaging's own test suite. It's dependency-free and AOT-safe. I found no maintained PEP 440 package for .NET; SemVer libraries (e.g. NuGet.Versioning) implement different rules.

### tkinter-tooltip → Avalonia `ToolTip`

- **Use:** about 30 `ToolTip(widget, text)` calls with default options. `_scanner.py:796/820` keeps a reference so it can update or destroy the tooltip later.
- **Defaults compared:**
  - tktooltip: `delay=0.0`, `follow=True` (the tooltip tracks the cursor), `x_offset=+10`, `y_offset=+10`; MIT-licensed code ([tooltip.py](https://github.com/gnikit/tkinter-tooltip/blob/main/tktooltip/tooltip.py)).
  - Avalonia `ToolTip.Tip`: `ShowDelay` 400 ms, placement `Pointer` (where the cursor stops), `HorizontalOffset` 0, `VerticalOffset` 20 ([Avalonia ToolTip](https://docs.avaloniaui.net/docs/reference/controls/tooltip)).
- **Parity:** cosmetic. Set `ToolTip.ShowDelay="0"` in a global style if instant tooltips matter. There's no built-in follow-the-cursor; it's not worth emulating unless asked.

### Pillow: unused

`grep -rniw "PIL|pillow|ImageTk"` matches only `pyproject.toml`. All images are PNGs loaded with Tk's native `PhotoImage(file=...)` (`cm_checker.py:69`). Avalonia's `Bitmap` loads PNG from `avares://` natively. Nothing to port.

### pywin32-ctypes: unused

It appears in no `import` in `src/`. pywin32 itself is used only for `win32api` in `utils.py`. Nothing to port.

## Stdlib with non-obvious mappings

### Text decoding of INIs and TXT files

- **Python behaviour** (`read_text(encoding="utf-8")`, errors=`strict`):
  - A leading BOM is **kept** as U+FEFF. *"any U+FEFF character in the decoded string (even if it's the first character) is treated as a ZERO WIDTH NO-BREAK SPACE"*; only `utf-8-sig` skips it ([codecs](https://docs.python.org/3/library/codecs.html)).
  - Invalid bytes **raise** `UnicodeDecodeError`.
  - Spike: a BOM file's first char was `U+FEFF`; `a=\xE4` raised.
- **Consequences in the current app:**
  - In a BOM-prefixed `Fallout4.ini`, the first line `﻿[General]` fails `startswith("[")`. Settings under it therefore land in `"NO-SECTION"`, and `general/slanguage` is missed.
  - A non-UTF-8 (ANSI) INI with any high byte makes `load_game_inis()` raise.
- **.NET defaults differ (spike):**
  - `File.ReadAllText(path)` and `ReadAllText(path, encoding)` both sniff the BOM and **strip it** ([ReadAllText](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.readalltext?view=net-10.0)). When a BOM is present they also **switch to their own UTF-8 decoder**: even `new UTF8Encoding(false, throwOnInvalidBytes: true)` did *not* throw on a BOM file with an invalid byte.
  - `Encoding.UTF8` replaces invalid bytes with U+FFFD instead of throwing.
- **Port:** `var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));`. This keeps the BOM as U+FEFF and throws `DecoderFallbackException` on invalid bytes. Whether to preserve the BOM and ANSI failure modes, or fix them, is a behaviour decision; flag it, don't silently change it.
- `app_settings.py:33` uses `errors="ignore"`, which **drops** invalid bytes. The .NET equivalent is a `UTF8Encoding` whose `DecoderFallback` replaces with `""` (`new DecoderReplacementFallback("")`). The default would insert U+FFFD instead.
- **Line splitting:** `str.splitlines()` breaks on `\n \r \r\n \v \f \x1c \x1d \x1e \x85    ` ([str.splitlines](https://docs.python.org/3/library/stdtypes.html)). `StringReader.ReadLine`/`File.ReadAllLines` break only on CR/LF/CRLF. Spike: Python counted 4 lines and .NET 2 for the same string. Write a `PySplitLines` helper with the same separator set and the same "no trailing empty line" rule.

### INI parsing: hand-rolled, not `configparser`

`configparser` is **not used anywhere**. Both readers are ad-hoc loops; port them line for line.

- **`load_game_inis()` (`game_info.py:84-102`):**
  - A section is a line where `line.startswith("[") and line.endswith("]")` (no strip), lower-cased.
  - Lines without `=` are skipped.
  - Key is `split("=",1)[0].lower()` (not stripped); value is the raw remainder (not stripped, quotes kept).
  - Duplicate keys: last one wins (dict overwrite).
  - No comment handling: `;foo=bar` becomes key `;foo`.
  - Lines before the first section go to `"NO-SECTION"`.
- **`read_mo2_ini()` (`mod_manager_info.py:131-162`):**
  - The section is the whole line, compared case-sensitively against `"[General]"`, `"[Settings]"` and `"[customExecutables]"`.
  - Keys are compared case-sensitively.
  - `@ByteArray(...)` is unwrapped with `value[11:-1]`.
  - In `[customExecutables]`, any key ending in `binary` is matched against tool exe suffixes.
- **Do not substitute a library.** `Microsoft.Extensions.Configuration.Ini`, for example, trims lines, keys and values, treats lines starting `;`, `#` or `/` as comments, strips surrounding quotes, uses case-insensitive keys, and **throws on duplicate keys** ([IniStreamConfigurationProvider.cs](https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Configuration.Ini/src/IniStreamConfigurationProvider.cs)). Each of those would change which settings the scanner sees in real Bethesda INIs.

### `csv.reader` for MO2 `skip_*` lists

`csv.reader((val,), doublequote=False, escapechar="\\", skipinitialspace=True)`, then `next(...)` (`mod_manager_info.py:175`). There is no BCL CSV reader, and `Microsoft.VisualBasic.FileIO.TextFieldParser` has no escape-character option. Hand-write a splitter: comma delimiter, `"` quoting, `\` escapes the next character, leading spaces after a delimiter skipped. Test it against Python output for MO2 values such as `.mohidden, "a,b"`.

### `zlib.crc32` → `System.IO.Hashing.Crc32`

- Python: `zlib.crc32(chunk, running)` over 64 KiB chunks, optionally seeking past the 12-byte BA2 header, formatted `f"{checksum:08X}"`. The values are matched against hard-coded CRC tables in the Downgrader and `BASE_FILES`.
- `System.IO.Hashing` is **not** in the shared framework: the spike found no `System.IO.Hashing.dll` under `Microsoft.NETCore.App/10.0.*`. It's an out-of-band Microsoft package, MIT, targeting net8/9/10 ([NuGet](https://www.nuget.org/packages/System.IO.Hashing)). AOT publish was clean in the spike.
- **Byte-order trap (spike):** for `"hello world"`, `GetCurrentHashAsUInt32().ToString("X8")` gives `0D4A1185`, matching Python. `Convert.ToHexString(GetCurrentHash())` gives `85114A0D`, little-endian bytes. Always use the UInt32 form.
- Alternative: a 256-entry table CRC-32 (IEEE, reflected, init/xorout `0xFFFFFFFF`) in about 20 lines removes the only NuGet dependency in this map.

### `struct`, `round()` and float formatting (HEDR check)

- `tabs/_overview.py:979-981`: `hedr = round(struct.unpack("<f", b)[0], 2)`, then `str(hedr) in MODULE_VERSION_SUPPORT[game]` against strings like `"0.94"`, `"1.0"` (Oblivion), `"1.70"` and `"1.2"`.
- `BinaryPrimitives.ReadSingleLittleEndian` → `(double)` → `Math.Round(x, 2, MidpointRounding.ToEven)` is fine for these values. Spike: Python's `round(float32(0.95), 2)` is `0.95`.
- **The trap is `str(float)`:**
  - Python prints `1.0` as `"1.0"` and `1.7` as `"1.7"`.
  - C# prints `1.0` as `"1"`, which breaks the Oblivion `"1.0"` match.
  - Emulate Python repr: shortest round-trip (`"R"`/default .NET Core formatting), plus append `".0"` when the result has no `.`, `e` or `inf`/`nan`.
  - Note that `"1.70"` can never match in Python either (an existing bug). Port it as-is and file it upstream if wanted.
- `read_uint` (`utils.py:427`) is `BinaryPrimitives.ReadUInt32LittleEndian`. Python raises `struct.error` on a short read; mirror that with an explicit length check.

### `json` (settings file)

`app_settings.py` uses `json.loads`/`json.dump` on a `TypedDict` and validates it with `typing.get_args`/`get_origin`. In .NET, use a `[JsonSerializable(typeof(AppSettings))]` source-generated context. Reflection-based `JsonSerializer` *"can break Native AOT apps"*, and is auto-disabled when `PublishTrimmed` is on ([STJ source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)). Set `JsonSerializerIsReflectionEnabledByDefault=false` so mistakes surface on CoreCLR too.

### `webbrowser.open` / `os.startfile`

Use `Process.Start(new ProcessStartInfo(urlOrPath) { UseShellExecute = true })`. The default is `false` on .NET (and `true` on .NET Framework), and with `false` only executables can be started ([UseShellExecute](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.useshellexecute?view=net-10.0)).

### `platform.release()` / `sys.getwindowsversion()`

- `PCInfo._get_os()` builds `f"{platform.system()} {platform.release()} {os_versions[build]}"`. CPython maps `(10, 0, 22000)` and later to release `"11"` ([platform.py](https://github.com/python/cpython/blob/3.14/Lib/platform.py); spike: `platform.release() == "11"` on build 26300). `tabs/_overview.py:119` then compares the result against the literal `"Windows 11 24H2"` to warn about MO2 <= 2.5.2.
- `Environment.OSVersion` on .NET 5+ returns the real version regardless of manifest ([breaking change note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/5.0/environment-osversion-returns-correct-version)). It reports `10.0.26300` (spike), so `release` must be derived as `Build >= 22000 ? "11" : "10"`.
- Note that build 26300 (this machine) isn't in `os_versions`, so the Python app already prints `"Windows 11 "` with a trailing space there. Port the table and the trailing space as-is.
- The `win11_24h2 = build >= 26100` gates (`utils.py:57`, `mod_manager_info.py:31`) map directly onto `Environment.OSVersion.Version.Build`.

### Obvious mappings (no research needed)

`threading.Thread`/`queue.Queue` → `Task` + `Channel<T>` or `Dispatcher.UIThread.Post`. `logging` → `Microsoft.Extensions.Logging` or a small file logger. `re` → `Regex` (the patterns in `helpers.py:48-49` are .NET-compatible; use `[GeneratedRegex]` for AOT). `shutil.copy2` → `File.Copy(…, overwrite: true)`. `stat.FILE_ATTRIBUTE_READONLY` / `chmod(S_IWRITE)` → `File.GetAttributes`/`SetAttributes`. `os.getenv("LOCALAPPDATA")` → `Environment.GetEnvironmentVariable`. `pathlib` → `System.IO.Path`/`FileInfo`.

## Risks, open questions and proposed follow-ups

1. **Ship a Windows 10 `supportedOS` app manifest** (packaging decision). Without it, `FileVersionInfo` returns version-lied numbers for OS binaries (proven in the spike). Proposed: a small ticket, or fold it into the packaging/AOT ticket.
2. **Parent-process lookup needs P/Invoke.** Decide between Toolhelp32 and NtQueryInformationProcess. Decide whether to also P/Invoke `QueryFullProcessImageNameW`, so the exe path matches psutil when MO2 and the toolkit run at different integrity levels. Proposed: a ticket for a `ProcessTree` helper with `[LibraryImport]`, tested with MO2 launching the app normally and elevated.
3. **PEP 440 comparer.** Needs its own small ticket with test vectors (`0.6.2-dev < 0.6.2`, `v0.6.2 == 0.6.2`, `1.2 == 1.2.0`, `.post`, `rc`). Using `System.Version` silently breaks the update check against the app's own `-dev` version.
4. **Text-decoding parity decision.** Decide whether to preserve Python's "BOM kept as U+FEFF / ANSI INI raises" behaviour or fix it. Either way, write it down; don't inherit .NET's silent defaults. Proposed: a behaviour-decision note on the map, plus a `PySplitLines` + strict-UTF-8 reader helper ticket.
5. **HTTP User-Agent and timeouts.** Set an explicit UA (GitHub requires one). Implement per-read timeouts for Downgrader downloads. Also decide whether to keep the unchecked-status / zero-`content-length` quirks.
6. **Python float repr emulation** for the HEDR version string match (`"1.0"` vs `"1"`). It's tiny, but easy to miss; add it to the plugin-scanner port ticket.
7. **F4SE DLL parsing via `PEReader`.** Validate against a corpus of real F4SE plugins (OG, NG and AE builds, including any with forwarded exports) and compare with Python's output. Decide how non-x64 DLLs should behave.
8. **CRC32: NuGet or hand-rolled?** `System.IO.Hashing` is Microsoft/MIT/AOT-clean. Hand-rolling removes the only new package. Either is fine; record the choice.
9. **Dark title bar.** Confirm in the UI ticket whether Avalonia already applies `DWMWA_USE_IMMERSIVE_DARK_MODE`; if not, add a `[LibraryImport]`.
10. **Dead dependencies to drop from the port:** `chardet` (its only caller is dead), `Pillow`, `pywin32-ctypes`.
