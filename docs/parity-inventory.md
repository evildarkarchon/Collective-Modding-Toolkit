# Parity inventory — Collective Modding Toolkit (Python reference implementation)

The checklist the port spec and its build slices are measured against. It records what the **Reference Implementation** in `src/` does at the **Parity Baseline** `f95a07c` (`wip-files/` excluded). Glossary: `GLOSSARY.md`; port decision: `docs/adr/0001-port-to-csharp-avalonia.md`.

Answers [Inventory the Python app's features and behaviours](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/6) on the [Port CMT to C# / Avalonia](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/2) map. The parity harness promoted it from the research branch to `main` ([ADR-0004](adr/0004-parity-proved-by-recorded-scenarios.md)). The coverage check in `parity/` reads the IDs from the tables below, so a new ID must go in the first column of a table whose first header is `ID`.

## How to read this

- Every checkable behaviour has an ID (`SHELL-3`, `OVW-12`, …), so build slices can name the items they cover in their acceptance criteria.
- **⚠ Trap**: a spot where a natural .NET translation would behave differently from the Python. The detail lives in [§ Parity traps](#parity-traps). Item IDs prefixed `T-`.
- **🐞 Bug**: behaviour that looks wrong. **Behaviour Parity** means the port reproduces it, and the bug is tracked as a separate follow-up issue. See [§ Suspected bugs](#suspected-bugs). Item IDs prefixed `B-`.
- `file:line` references are at `f95a07c`.
- Data tables (CRC/version maps, whitelists, tooltip texts) are ported **verbatim** from the named source. This document points at them rather than copying them, so there is only one source of truth.

---

## 1. Process, startup and shell

| ID | Behaviour | Source |
|---|---|---|
| SHELL-1 | Logging opens `cm-toolkit.log` in the **current working directory** in append mode, format `%(levelname)s : %(message)s`, at level INFO. The first three lines are always written at INFO: a dash rule the same length as the start message, `Starting Collective Modding Toolkit v0.6.2-dev`, and the timestamp `%Y-%m-%d %H:%M`. | `main.py:31-40` |
| SHELL-2 | Settings load next (§2). After that, the root logger level is set from `log_level`, so the three start lines are written regardless of the configured level. | `main.py:42-43` |
| SHELL-3 | Cascadia Mono (`assets/fonts/CascadiaMono.ttf`) is loaded process-private with `AddFontResourceExW(path, FR_PRIVATE=0x10, 0)`. | `utils.py:155-160` |
| SHELL-4 | The root window starts withdrawn. `sys.stderr` is replaced by the **StdErr window** (SHELL-12). The checker builds, the theme is applied, then the window is shown. | `main.py:46-56` |
| SHELL-5 | Construction order: PC info (§4.1) → mod manager detection (§4.2) → game path (§4.4) → game INIs (§4.5) → main window → **synchronous update check** (§10), which runs before the window is shown. | `cm_checker.py:51-65` |
| SHELL-6 | Main window: title `Collective Modding Toolkit v0.6.2-dev`, icon `images/icon-32.png`, fixed size **760×450**, not resizable, not fullscreen, centred on the primary screen (`(screen − size) // 2`). | `cm_checker.py:79-88`, `globals.py:68-69` |
| SHELL-7 | Grid layout: row 0 holds the optional update banner (§10), row 1 holds the tab strip (weight 1). Tabs, in order: **Overview, F4SE, Scanner, Tools, Settings, About**. | `cm_checker.py:89-103` |
| SHELL-8 | Tabs **load lazily** the first time they are selected. Overview loads at startup because it is the initially selected tab. Lifecycle (`CMCTabFrame.load`): a centred loading label in 20pt shows the tab's `loading_text` (F4SE: `Scanning DLLs...`; others empty) → `_load()` → on success the label is removed and the GUI is built. On failure the label shows `loading_error` (default `Failed to load tab.`) in `COLOR_BAD`, and the tab is **never retried** during the session. Each later switch calls `_switch_to()`; leaving a tab calls `switch_from()`. | `helpers.py:147-217` |
| SHELL-9 | Closing the window (WM_DELETE) is ignored while `CMChecker.processing_data` is set. Nothing ever sets that flag, so in practice closing always works. On close, stderr is restored and the root destroyed. | `cm_checker.py:73-77` |
| SHELL-10 | **Escape** on the main window calls `root.destroy()` directly, bypassing SHELL-9. 🐞 B-12 | `cm_checker.py:105` |
| SHELL-11 | Minimising the main window withdraws the Scanner's side and details panes (if that tab is loaded). Restoring re-shows them. | `cm_checker.py:185-203` |
| SHELL-12 | **StdErr window**: writes to stderr are buffered. On `flush()`, a Toplevel `An Error Occurred` is created if it doesn't exist (Text widget, 120×25 chars) and the buffered text is appended. The text is also logged as `ERROR : StdErr : <text>`. Closing the window discards it, and the next flush creates a new one. When this actually surfaces, and from which thread, is the subject of [Decide how Behaviour Parity treats crashes and error surfacing](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/18). | `helpers.py:287-317` |
| SHELL-13 | Exceptions raised during construction (for example the MO2 `FileNotFoundError`/`ValueError` in §4.3) propagate before `mainloop`. The process exits with the traceback in the log via StdErr, and **no window is shown**. → #18 | `main.py:51` |
| SHELL-14 | Theme: sv_ttk `sun-valley-dark`. The tab layout is overridden to drop the focus ring. Tab font is 12pt; buttons, checkbuttons, radiobuttons, labelframe labels, treeview and headings use 10pt; all in `COLOR_DEFAULT`. `Update.TFrame` has a pale green background. | `utils.py:325-354` |
| SHELL-15 | Dark title bar via `DwmSetWindowAttribute(hwnd, 19 and 20, 1)` on the main window and every modal (§9). | `utils.py:318-322` |
| SHELL-16 | Assets are resolved relative to `sys._MEIPASS` (frozen) or `.`: `<base>/assets/<relative>`. | `utils.py:199-202` |

**Fonts**: Cascadia Mono 10 (`FONT_SMALL`), 12 (`FONT`), 20 (`FONT_LARGE`). `FONT_SMALLER` (8) is unused.
**Colours** (`globals.py:32-40`): default `#CACACA`, good `#619267`, bad `#AF5A66`, info `dodger blue`, neutral-1 `gray`, neutral-2 `bisque`, warning `orange`, note `#C5C464`, indie `#4b92da`. Downgrader-only: OG `dodger blue`, NG `SlateBlue1`, AE `salmon`. These are Tk colour names, so their RGB values need pinning in the port.

## 2. Settings (`settings.json`)

| ID | Behaviour | Source |
|---|---|---|
| SET-1 | The file is `settings.json` in the **CWD**, UTF-8. Keys and defaults, in file order: `log_level` (`INFO`), `update_source` (from `assets/download-source.txt`), `scanner_OverviewIssues`, `scanner_Errors`, `scanner_WrongFormat`, `scanner_LoosePrevis`, `scanner_JunkFiles`, `scanner_ProblemOverrides`, `scanner_RaceSubgraphs`, `downgrader_keep_backups`, `downgrader_delete_deltas` (all `true`). | `app_settings.py:44-70` |
| SET-2 | `download-source.txt` is read as UTF-8 with errors ignored and stripped. It must be `nexus` or `github`; anything else logs an error and becomes `nexus`. A read failure logs the exception and becomes `nexus`. The repo ships `github`. | `app_settings.py:31-41` |
| SET-3 | Missing file → log `Settings : settings.json not found; using defaults.` and write the defaults. | `app_settings.py:77-80` |
| SET-4 | Unparseable JSON, or JSON whose top level isn't an object → log the exception and **reset everything** to defaults, then resave. | `app_settings.py:83-90` |
| SET-5 | Per-key validation. A key missing from the file is added and the file resaved (`Adding new settings to JSON: a, b`). An unknown key is dropped and the file resaved (error `Unknown setting '<k>' will be removed.`). A `Literal` key (`log_level` ∈ DEBUG/INFO/WARNING/ERROR, `update_source` ∈ nexus/github/both/none) with an invalid value keeps the default and resaves. A bool key requires `type(v) is bool` exactly, so `1`/`0` are rejected and reset. | `app_settings.py:91-128` |
| SET-6 | Save: `json.dump(indent="\t")` plus a trailing `\n`, key order as SET-1, `ensure_ascii=True` (Python default). Save failures are logged and swallowed. ⚠ T-10 | `app_settings.py:130-137` |
| SET-7 | Writers: the Settings tab (on each radio click), the Scanner (on each scan, only if a checkbox differs from the stored value), and the Downgrader (on Patch All, only if a checkbox changed). | `_settings.py:105-107`, `scan_settings.py:128-140`, `downgrader.py:279-288` |
| SET-8 | `scanner_*` values are **written but never read**. The Scanner's checkboxes always start ticked. 🐞 B-1 | `_scanner.py:617-619` |

## 3. Log file (`cm-toolkit.log`)

| ID | Behaviour |
|---|---|
| LOG-1 | One shared root logger. `Logger` widgets (Downgrader, Archive Patcher) also send every displayed message to the file: `ERROR` for Bad, `INFO` otherwise, unless `skip_logging=True`. |
| LOG-2 | Message prefixes used throughout: `Settings : …`, `Update Check : <source> : …`, `Switch Tab : <Class>`, `Load Tab : <Class>[ : Failed : <err>]`, `Refresh Tab : <Tab>`, `Reset Info: Binaries/Modules/Archives`, `Gathering Info: …`, `Get File Info: <path>`, `Archives: …`, `Scanning <dll>`, `Patcher Running: ArchivePatcher`, `Files: n \| Version: v \| Filter: f`, `Patcher Finished`, `Auto-Fix : Running …`, `StdErr : …`, `Added SimpleProblemInfo: Path: … \| Problem: …`, `get_cpu():` / `get_gpu():` (with traceback). The build should `grep` the reference for `logger.` to capture exact text. |
| LOG-3 | The Downgrader's progress chatter uses `print()`, which goes to stdout. That is `None` in the windowed exe, so **none of it is visible or logged**. Don't port it as log lines. |

## 4. Environment detection

### 4.1 PC info (Overview "PC Specs")

| ID | Behaviour | Source |
|---|---|---|
| PC-1 | Wine detection: `ntdll` exports `wine_get_version` → OS string `Linux (WINE)`. | `helpers.py:69-70` |
| PC-2 | OS string is `f"{platform.system()} {platform.release()} {os_versions.get(build, '')}"`, for example `Windows 11 24H2`. An unknown build leaves a trailing space (`Windows 11 `). The build → release table runs from 18362 to 26200. ⚠ T-6 | `helpers.py:51-80` |
| PC-3 | RAM is total physical memory / 1024³, `round()`ed (banker's rounding). ⚠ T-11 | `helpers.py:82-87` |
| PC-4 | CPU comes from `HKLM\Hardware\Description\System\CentralProcessor\0\ProcessorNameString`. If it contains `Intel` but doesn't start with it, it is rewritten as `Intel <rest without 'Intel'>`. Then regex `(?:\d+(?:th\|rd\|nd) Gen\| ?Processor\| ?CPU\|\d*[- ]Core\|\(TM\)\|\(R\))` is removed, whitespace collapsed, everything from the last `@` dropped, and the result stripped. Failure → `Unknown CPU` plus a logged exception. | `helpers.py:89-105` |
| PC-5 | GPU: `HKLM\HARDWARE\DEVICEMAP\VIDEO` value `\Device\Video0`, strip the `\Registry\Machine\` prefix, open that key, read `HardwareInformation.AdapterString` (REG_SZ, stripped) and `HardwareInformation.qwMemorySize` (REG_QWORD, GiB `round()`ed). Defaults are `Unknown GPU` / 0. | `helpers.py:107-125` |
| PC-6 | Display: line 1 is `"{os}\n{ram}GB RAM"`, line 2 is `"{cpu}\n{gpu} {vram}GB"`. | `cm_checker.py:57-58` |

### 4.2 Mod manager detection

| ID | Behaviour | Source |
|---|---|---|
| MM-1 | Walk up to **8** ancestors starting at the parent process. The first one whose name is exactly `ModOrganizer.exe` or `Vortex.exe` wins. The name is `Mod Organizer` or `Vortex`, the path is the process exe, and the version is the first 3 parts of its file version (`0.0.0` if it has none). ⚠ T-5 | `utils.py:177-196` |
| MM-2 | No manager is a valid state: Overview shows `Not Found` (COLOR_BAD, tooltip `Your mod manager must launch the app to be detected.`), and the Overview problem `No Mod Manager` is added (OVW-P1). | |

### 4.3 MO2 configuration

| ID | Behaviour | Source |
|---|---|---|
| MO2-1 | **Portable**: if `<mo2>/portable.txt` exists, `<mo2>/ModOrganizer.ini` must exist, otherwise raise `FileNotFoundError("portable.txt found but no ModOrganizer.ini found in MO2 install path")`. Read it and mark the install portable. | `game_info.py:177-189` |
| MO2-2 | **Instance**: otherwise read `HKCU\Software\Mod Organizer Team\Mod Organizer\CurrentInstance` (REG_SZ) and read `%LOCALAPPDATA%\ModOrganizer\<instance>\ModOrganizer.ini` if it exists. | `game_info.py:191-201` |
| MO2-3 | **Fallback**: if no `gamePath` was obtained, read the portable INI if it exists and mark the install portable. Otherwise raise `FileNotFoundError("Unable to find ModOrganizer.ini. Please report this along with your MO2 instance details.")`. | `game_info.py:203-208` |
| MO2-4 | INI parse: UTF-8 strict (⚠ T-1), `splitlines` (⚠ T-1). A line starting with `[` switches section, and only `[General]`, `[Settings]` and `[customExecutables]` are tracked. Each line is split on the first `=`, and **keys and values are not trimmed**. A value wrapped in `@ByteArray(…)` is unwrapped. | `mod_manager_info.py:131-162` |
| MO2-5 | Keys read: `[General]` `gameName`, `gamePath`, `selected_profile`; `[Settings]` `base_directory`, `cache_directory`, `download_directory`, `mod_directory`, `overwrite_directory`, `profile_local_inis`, `profile_local_saves`, `profiles_directory`, `skip_file_suffixes`, `skip_directories`. Defaults are `base_directory` = INI folder and the other directories `%BASE_DIR%/webcache\|downloads\|mods\|overwrite\|profiles`, suffixes `(.mohidden,)`, skip dirs empty. | `mod_manager_info.py:97-129` |
| MO2-6 | Path resolution: a key ending in `directory` or `Path` that contains `%BASE_DIR%` becomes `base / value.replace('%BASE_DIR%', '')`, which is a *rooted* relative path, so it lands on the drive root. 🐞 B-6, ⚠ T-7. Values without the placeholder become `Path(value)`, which collapses `\\`. ⚠ T-7 | `mod_manager_info.py:164-173` |
| MO2-7 | `skip_*` values are CSV-parsed (`escapechar='\\'`, `skipinitialspace`), even when the value is the Python default tuple or set. 🐞 B-5. Both are lowercased afterwards. | `mod_manager_info.py:174-176,194-195` |
| MO2-8 | `gameName` other than `Fallout 4` → `ValueError("Only Fallout 4 is supported.\ngameName is '<x>' in INI: \n<path>")`. A missing `selected_profile` → `ValueError("Profile is not set in ModOrganizer.ini.")`. Both are fatal at startup (SHELL-13). | `mod_manager_info.py:178-185` |
| MO2-9 | Exposed: `game_path`, `selected_profile`, `stage_path` (= `mod_directory`), `overwrite_path`, `profiles_path`, `skip_file_suffixes`, `skip_directories`, `portable`, `portable_txt_path`, `ini_path`, and the raw `mo2_settings` dict, which is shown verbatim in OVW-4. | |
| MO2-10 | `[customExecutables]` `*binary` entries ending in `xedit.exe`/`fo4edit.exe`/`bsarch.exe` are collected into `executables`, along with a sibling `BSArch.exe` for xEdit. **Nothing reads `executables`**, so this is dead (§13). | `mod_manager_info.py:142-156` |
| MO2-11 | Vortex: no configuration is read. The manager only contributes its name, path and version. | `game_info.py:210-211` |

### 4.4 Game path

| ID | Behaviour | Source |
|---|---|---|
| GP-1 | Priority: MO2 `gamePath` → CWD (if it is an FO4 dir) → `HKLM\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4\Installed Path` → `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1998527297\path` → ask the user. An "FO4 dir" is a directory containing `Fallout4.exe`. The MO2 path is **not validated**. | `game_info.py:175-275` |
| GP-2 | Ask: yes/no box `Fallout 4 Not Found` / `Your Fallout 4 installation could not be detected.\nThis is usually due to the game being moved or the launcher not being run once from its current location.\n\nManually specify a location?\nCM Toolkit will close otherwise.`. **No** → exit. **Yes** → file dialog `Select Fallout4.exe` filtered to `Fallout 4` / `Fallout4.exe`. Cancel → error `Game not found` / `A Fallout 4 installation could not be found.`, then exit. | `game_info.py:232-256` |
| GP-3 | A file path is reduced to its parent folder. If that isn't an FO4 dir: error `Game not found` with the registry hint text when a registry path existed (`…The path set in your registry is:\n<path>\n\nIf this is not correct, please run the Fallout 4 Launcher to correct it.`), otherwise the plain text. Then exit. | `game_info.py:258-273` |
| GP-4 | Derived paths: `Data` (if it is a dir), `Data/F4SE/Plugins` (if it is a dir), otherwise `None`. | `game_info.py:144-156` |

### 4.5 Game INIs

| ID | Behaviour | Source |
|---|---|---|
| INI-1 | Folder: `SHGetFolderPathW(CSIDL_PERSONAL)` + `My Games\Fallout4`. If that folder doesn't exist, `FileNotFoundError("Folder does not exist:\n<path>")` is fatal. | `game_info.py:82-84`, `utils.py:163-170` |
| INI-2 | Files are read in order `Fallout4.ini`, `Fallout4Prefs.ini`, `Fallout4Custom.ini`, skipping missing ones. Prefs go to `game_prefs`; the other two are **merged** into `game_settings`, with Custom overriding. Under MO2 these reads go through the VFS, which supplies profile-local INIs (⚠ T-12). | `game_info.py:83-101` |
| INI-3 | Parse: UTF-8 strict, `splitlines`. A section header is a line with `[` at the start and `]` at the end, lowercased. Lines before any header go under `NO-SECTION`. A key is `split('=', 1)`; the key is lowercased, and **neither key nor value is trimmed**. Lines without `=` are ignored. 🐞 B-14, ⚠ T-1 | `game_info.py:89-101` |
| INI-4 | Language: `[General] sLanguage` lowercased, mapped to `cn de en es esmx fr it ja pl ptbr ru`, anything else `en`. BA2 suffixes are `main`, `textures`, `voices_en`, plus `voices_<lang>` when the language isn't English. | `game_info.py:103-111` |

## 5. Overview tab

### 5.1 Layout

| ID | Behaviour |
|---|---|
| OVW-1 | Top block: right-aligned labels `Mod Manager:` / `Game Path:` / `Version:` / `PC Specs:` (12pt). Values are in column 2, and a 32px refresh button spans rows 0-1 on the right (tooltip `Refresh`). Below that, three label frames sit side by side and fill the area: **Binaries (EXE/DLL/BIN)**, **Archives (BA2)**, **Modules (ESM/ESL/ESP)**. |
| OVW-2 | Mod Manager value is `"{name} v{version} [Profile: {selected_profile or 'Unknown'}]"` in neutral-2, or `Not Found` in bad with its tooltip. |
| OVW-3 | MO2 shows an info icon with tooltip `Detection details`. Vortex shows a warning icon with tooltip `Note: Vortex is not yet fully supported.\nOverview should be accurate but Scanner will only look in Data and not your staging folders, so it cannot yet identify the source mod for each issue.` |
| OVW-4 | Clicking the MO2 info icon opens an AboutWindow, 750×350, `Detected Mod Manager Settings`, text `EXE: …\nINI: …\nPortable: True/False\n[Portable.txt: …\n]` followed by every `mo2_settings` entry as `key.rjust(maxlen): value`. ⚠ T-9: the values are rendered with Python `str()`, so you get `True`, `WindowsPath`→backslash paths, and list repr `['.mohidden']`. |
| OVW-5 | MO2 on `pc.os == "Windows 11 24H2"` **exactly** with version ≤ 2.5.2 shows a warning icon on the PC Specs row. Tooltip: `Note: MO2 2.5.2 and earlier have issues on Windows 11 24H2+.\nPython apps such as Wrye Bash and CLASSIC may give errors\nsuch as FileNotFound or fail to detect files that are only\npresent in the VFS and not the Data folder.` 🐞 B-8 |
| OVW-6 | Game Path is a clickable label (hand cursor, tooltip `Click to open folder`) that opens the folder in Explorer (`os.startfile`). |
| OVW-7 | Version shows the **Install Type** display string in `COLOR_GOOD`, bound live to the game state. PC Specs is two labels side by side, 30px apart. |
| OVW-8 | **Refresh** re-runs binaries → modules → archives and rebuilds the three frames, but not the top block, which updates through bound variables. It shows no message boxes (OVW-M3/M5). |

### 5.2 Binaries

| ID | Behaviour |
|---|---|
| OVW-B1 | Files are checked in `BASE_FILES` order (`globals.py:85-143`): `Fallout4.exe`, `Fallout4Launcher.exe`, `steam_api64.dll`, `f4se_loader.exe`, `f4se_steam_loader.dll`, `CreationKit.exe`, `Tools\Archive2\Archive2.exe`. Rows are labelled with the name minus its extension and a colon (`Archive2:`). |
| OVW-B2 | Per file: missing → `Not Found`. Otherwise read the file version resource (⚠ T-4) and the **full-file CRC32** (uppercase 8-hex). When a version exists, look up by the version string `a.b.c.d`, then by CRC. With no version, look up by CRC, and the "version string" becomes the CRC. A miss gives `Unknown`. |
| OVW-B3 | `Next-Gen & Anniversary` (steam_api64) is replaced by the game's Install Type when the game is Next-Gen or Anniversary. |
| OVW-B4 | `Fallout4.exe`'s result **becomes the game's Install Type**. `Unknown` adds problem OVW-P2. |
| OVW-B5 | Address Library: `Data/F4SE/Plugins/version-<VersionString with . → ->.bin`. Present → `Installed` (good). Absent → `Not Found` (bad, tooltip `Address Library is required for many F4SE mods.`) plus problem OVW-P3. Only checked when Data exists. |
| OVW-B6 | **Down-Grade detection**: when the game is Old-Gen or Down-Grade and Data exists, CRC32 `Data/Fallout4 - Startup.ba2` **skipping its first 12 bytes**. A match on `A5808F5F` makes the Install Type **Down-Grade**. A missing file adds problem OVW-P4. |
| OVW-B7 | Row colour and problems, evaluated **in this order**. (a) The type equals the game's Install Type → good. 🐞 B-13: this includes `Unknown == Unknown`. (b) Old-Gen → good if the game is Down-Grade, otherwise bad plus `Wrong Version` (OVW-P5). (c) Not Found → neutral-1 for `CreationKit.exe`, `Archive2.exe`, and for `f4se_steam_loader.dll` on Next-Gen/Anniversary, otherwise bad plus `File Not Found` (OVW-P6). (d) Anything else → bad plus `Wrong Version`. |
| OVW-B8 | Hovering a value swaps the label text to the version tuple `a.b.c.d` (or `Not Found` when there is no version resource) and restores it on leave. The label is 11 chars wide. |
| OVW-B9 | A `Downgrade Manager...` button sits at the bottom of the frame and opens §9.3. |

### 5.3 Modules (computed before archives)

| ID | Behaviour |
|---|---|
| OVW-M1 | No Data folder → problem OVW-P7 and stop. |
| OVW-M2 | The enabled list starts with the **game masters** (`globals.py:145-155`, including `fallout4_vr.esm`) that `exist` in Data. |
| OVW-M3 | `<game>/Fallout4.ccc`: each UTF-8 line that is a file in Data is appended. If the file is missing → problem OVW-P8, plus (initial load only) a warning box `Warning` / `Fallout4.ccc not found.\nCC files may not be detected. Verifying Steam files or reinstalling should fix this.` |
| OVW-M4 | `%LOCALAPPDATA%\Fallout4\plugins.txt` (UTF-8; redirected to the profile under MO2's VFS): every line starting with `*` whose remainder is a file in Data is appended. **Not de-duplicated** against masters or CC. 🐞 B-17 (suspected) |
| OVW-M5 | plugins.txt unreadable or missing → problem OVW-P9, plus (initial load only) the warning `plugins.txt not found.\nEnable state of plugins can't be detected.\nCounts will reflect all modules/archives in Data, which is likely higher than your actual counts.` Then every `.esp/.esl/.esm` in Data that isn't already listed is appended (case-insensitive path equality, ⚠ T-8). |
| OVW-M6 | Each module reads its first 34 bytes. A read error → unreadable plus OVW-P10. Fewer than 34 bytes or no `TES4` magic → unreadable plus OVW-P11. Bytes 24-27 not `HEDR` → unreadable with **no problem entry**, and the module is not counted. |
| OVW-M7 | HEDR version is the bytes at 30-33. `33 33 73 3F` → v0.95. `00 00 80 3F` → v1.00. Anything else: the float is `round(…, 2)` and recorded as unknown, with problem OVW-P12, which lists the games whose support set contains `str(hedr)` (`globals.py:54-66`). ⚠ T-3, 🐞 B-7 |
| OVW-M8 | Light means flags (bytes 8-11, LE uint32) `& 0x200` **or** the `.esl` extension. Everything else is Full. Unknown-HEDR modules are still counted. |
| OVW-M9 | Rows: `Full:` / `Light:` / `Total:` with counts against ` /  254`, ` / 4096`, ` / 4350` (note the double space on 254). `Unreadable:` (bad if > 0, otherwise neutral-1). A separator, then `HEDR v1.00:`, `HEDR v0.95:`, `HEDR v????:` (bad if > 0). Each label has the tooltip from `globals.py:266-290`. |
| OVW-M10 | When unknown-HEDR modules exist, an info icon (`Detection details`) opens a TreeWindow (§9.2): 400×500, `Detected Invalid Module Versions`, headers `HEDR` / ` Module`, rows of (version, path) sorted by version descending. |

### 5.4 Archives

| ID | Behaviour |
|---|---|
| OVW-A1 | No Data folder → stop. That case is already reported by OVW-M1. |
| OVW-A2 | `game_settings['archive']` missing → `ValueError("Archive section missing from INIs")`. The Overview load fails (SHELL-8 / #18). 🐞 B-15 |
| OVW-A3 | Enabled BA2s, case-insensitive set (⚠ T-8): (1) comma-split, stripped entries of `sResourceIndexFileList`, `sResourceStartUpArchiveList`, `sResourceArchiveList`, `sResourceArchiveList2` that are files in Data. (2) For every enabled module, `<stem> - <suffix>.ba2` for each language suffix (INI-4) that exists beside it. (3) `Fallout4 - Nvflex.ba2` when `[NVFlex] bNVFlexEnable` is exactly `"1"` (otherwise problem OVW-P13 if it is missing). (4) For Anniversary, `Fallout4 - TexturesPatch.ba2` (otherwise problem OVW-P14 if it is missing). |
| OVW-A4 | Each archive reads its first 12 bytes. A read error, a short read, or no `BTDX` → unreadable plus OVW-P15/P16. Byte 4 = 1 → OG. Byte 4 = 7 or 8 → NG. Any other value → unreadable plus OVW-P17. Bytes 8-11 `GNRL` / `DX10` are counted; anything else → unreadable plus OVW-P18 (decoded as UTF-8; ⚠ T-1). |
| OVW-A5 | Rows: `General:` / `Texture:` / `Total:` with counts against ` / 256`, ` / 255`, ` / 511`. For Anniversary the limits are 1024 / 1023 / 2047 (⚠ T-2: the code checks `{Obsolete2, AE2}`). `Unreadable:`, a separator, then `v1 (OG):` / `v7/8 (NG):`. Tooltips are in `globals.py:255-265`. |
| OVW-A6 | An `Archive Patcher...` button sits at the bottom of the frame and opens §9.4. |

### 5.5 Count colouring and limit problems

| ID | Behaviour |
|---|---|
| OVW-C1 | Counts are right-justified to 4 chars. `num < int(0.95 × limit)` → good. `≤ limit` → `orange`. `> limit` → bad. |
| OVW-C2 | Over the limit, for non-Total rows only, a SimpleProblem is added: path `"{n} {General\|Texture\|Full\|Light} {Archive\|Module}s"`, problem `Limit Exceeded`, summary `You have {n} {fmt} {type}s enabled. The limit is {limit}.`. Solution and extra data per type are in `_overview.py:592-603` (Unpackrr link, the ESL guide link, or no link for Light). |

### 5.6 Overview problems (fed to the Scanner)

Collected in a shared list, cleared on every load/refresh. Most problems are added while the data is gathered (`_load`/`refresh`), in the order binaries → modules → archives. The rest are appended afterwards, while the three frames are built, in frame order: the per-binary `Wrong Version` / `File Not Found` problems (OVW-P5, P6, from `build_gui_binaries`), then the archive limits (General, Texture), then the module limits (Full, Light) (OVW-C2, from `add_count_label`). So the full order is: gather binaries → modules → archives → per-binary Wrong Version / File Not Found → archive limits → module limits. The Scanner's stable sort (SCN-R3) makes this order observable. `ProblemInfo.mod` is `"OVERVIEW"` for archive and module header problems (remapped by SCN-R2), and `None`, which becomes `<Unmanaged>`, for the rest; `File Not Found` problems get `""`.

| ID | Type (tree group) | Path shown | Summary / solution |
|---|---|---|---|
| OVW-P1 | `No Mod Manager` | exe name (`argv[0]`) | `No Mod Manager Detected` / the no-manager tooltip text |
| OVW-P2 | `Unknown Game Version` | `Fallout4.exe` | `_overview.py:683` / `Either update the game/verify files in Steam, or report this issue.` |
| OVW-P3 | `File Not Found` | `F4SE\Plugins\version-….bin` | Address Library text / `Download the mod here:` + Nexus 47327 link |
| OVW-P4 | `File Not Found` | `Fallout4 - Startup.ba2` | `_overview.py:721` / VerifyFiles |
| OVW-P5, P6 | `Wrong Version` / `File Not Found` | file name | `_overview.py:263,281` / none |
| OVW-P7, P8, P9 | `File Not Found` | `Data` / `Fallout4.ccc` / `plugins.txt` | `_overview.py:881,899,917`. P9's solution is `N/A` when a manager is present, otherwise `Launch this app with your mod manager.` |
| OVW-P10, P11, P12 | `Invalid Module` | module name | `_overview.py:949,963,989-990` |
| OVW-P13, P14 | `File Not Found` | BA2 name | `_overview.py:770,784` / VerifyFiles |
| OVW-P15–P18 | `Invalid Archive` | BA2 name | `_overview.py:803,817,839,860` |

## 6. F4SE tab

| ID | Behaviour | Source |
|---|---|---|
| F4SE-1 | Load errors (SHELL-8): `Data folder not found`; `Data/F4SE/Plugins folder not found`, with `\nTry launching via your mod manager.` appended when no manager is detected. | `_f4se.py:50-59` |
| F4SE-2 | Every entry in `F4SE/Plugins` (not recursive, in directory order) with a `.dll` suffix (case-insensitive) whose name doesn't start with `msdia` (**case-sensitive**) is inspected. | `_f4se.py:61-65` |
| F4SE-3 | Inspection loads the DLL with `LoadLibraryExW(DONT_RESOLVE_DLL_REFERENCES)` and checks exports. `F4SEPlugin_Load` or `F4SEPlugin_Preload` → F4SE plugin. `F4SEPlugin_Query` → supports OG. `F4SEPlugin_Version` → supports NG/AE, and its data struct (`utils.py:62-75`) gives address-independent `= addressIndependence & 0b110`, struct-independent likewise, supports NG `= compatibleVersions ∋ 0x010A3D40 or 0x010A3D80`, supports AE `= any(v > 0x010B0890)`. Supports NG/AE stays *None* (not False) otherwise. A load failure raises and the tab load fails. ⚠ T-5, 🐞 B-18 | `utils.py:242-273` |
| F4SE-4 | Tree: columns `DLL` (240, right-aligned), `OG` / `NG` / `AE` (60, centred), `Your Game` (80). Non-F4SE DLLs show `❓❓❓` in the first three columns, leave `Your Game` blank, and use the neutral tag. | `_f4se.py:75-135` |
| F4SE-5 | OG cell: `✔` if it supports OG, otherwise empty. NG/AE cells, when it supports NG/AE: `✳` if address- **and** struct-independent, `⚠` if the flag is None, `✔` if true, empty if false. Without NG/AE support they are empty. | `_f4se.py:137-166` |
| F4SE-6 | `Your Game` cell, default `❌`. On Anniversary: ✳ if independent, ✔ if it supports AE, ⚠ if it supports NG/AE, otherwise ❌. On Next-Gen: the same using supports NG. On Old-Gen/Down-Grade: ✳ if independent, ✔ if it supports OG, otherwise ❌. The row tag (colour) follows the cell: ✳ indie, ⚠ note, ✔ good, anything else bad. | `_f4se.py:169-210` |
| F4SE-7 | Right side: heading `F4SE DLLs` (12pt) above a read-only Text holding `ABOUT_F4SE_DLLS` (`globals.py:228-246`). Coloured runs: line 2 chars 0-18 neutral-2, and the icon glyph at the start of lines 6, 8, 10 and 14 in good / indie / neutral-2 / note. | `_f4se.py:104-128` |

## 7. Scanner tab

### 7.1 Layout and panes

| ID | Behaviour | Source |
|---|---|---|
| SCN-1 | Load error: `Data folder not found`. | `_scanner.py:108-112` |
| SCN-2 | Main area: buttons `Collapse All` / `Expand All`, and a right-aligned results info label (10pt, neutral-2). Below that the results tree. In "stage mode" (manager has a stage path) it gets a right-aligned `mod` column. Selection is disabled until results exist. A progress bar sits at the bottom. A scanning-status label (12pt) appears above the progress bar only while scanning. | `_scanner.py:118-173` |
| SCN-3 | **Side pane**: a borderless Toplevel (2px groove), 200 wide, at `root_x + root_w`, `root_y + 40`, height `root_h − 45`. It holds a `Scan Settings` labelframe with seven checkboxes (labels and tooltips in `scan_settings.py:98-105`; **all start ticked**, 🐞 B-1) and an accent `Scan Game` button. Scan Game is disabled while no box is ticked. | `_scanner.py:602-659` |
| SCN-4 | **Details pane**: a borderless Toplevel, root width × 200, directly below the root. Created on the first result selection. | `_scanner.py:662-910` |
| SCN-5 | Pane lifetime: the side pane is created on switching to the tab. Both panes are destroyed when leaving the tab, and the selection is cleared. Both are repositioned on the root's `<Configure>` and raised on the root's `<FocusIn>`. Focusing a pane raises the root and then the other pane. They are withdrawn and restored together with the root (SHELL-11). Design question: [Prototype the Scanner's docked side and details panes](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/12). | `_scanner.py:76-106` |

### 7.2 Scan run (threaded)

| ID | Behaviour | Source |
|---|---|---|
| SCN-S1 | Start: the button shows `Scanning...` and is disabled. The tree, its data and the info label are cleared, and the details pane is destroyed. Status `Refreshing Overview...`, then **Overview refresh** (OVW-8) runs on the UI thread. | `_scanner.py:175-200` |
| SCN-S2 | Settings snapshot (`ScanSettings`): checkbox values, saved per SET-7. `skip_data_scan` is true unless some ticked box is a data check (anything except Overview Issues and Race Subgraphs). Skip suffixes are MO2's (MO2-7) plus `.vortex_backup`; for non-MO2, just `.vortex_backup`. Skip dirs are `{bodyslide, fo4edit, robco_patcher, source}` ∪ MO2's. | `scan_settings.py:116-149` |
| SCN-S3 | If Overview Issues is ticked, the results are seeded with the Overview problems. Progress is set to 1. Status becomes `Building mod file index...` when a data scan will run. A worker thread starts, and the UI polls its queue every **100 ms** with `after()`. | `_scanner.py:202-210` |
| SCN-S4 | Queue protocol: a `tuple` holds the top-level Data folder names. A `str` holds the current top-level folder, giving status `Scanning... {index}/{max(1,n)}: {name}` with a 0-based index and progress `index/n × 100`; a name not in the tuple is ignored. A `list` holds the problems, appended. The worker ends by setting `thread_scan = None` and **then** enqueuing its problems. 🐞 B-2. The UI checks for `thread_scan is None` after draining, then sets progress to 100 and populates. A worker exception never clears the flag. 🐞 B-3 | `_scanner.py:212-239,409-411,598-599` |
| SCN-S5 | **Race subgraphs** (if ticked; first on the worker): the progress string `Race Subgraph Records`. For each enabled module (OVW-M2..M5), count occurrences of `00 53 41 44 44` in the **whole file**, skipping unreadable files. A total **> 100** adds a SimpleProblem: path `{total} SADD Records from {n} modules`, problem `Race Subgraph Record Count`, summary `INFO_SCAN_RACE_SUBGRAPHS`, solution `_scanner.py:404`, file list of (count, path). | `_scanner.py:383-407` |
| SCN-S6 | **Mod file index** (MO2 stage mode only): `profiles/<profile>/modlist.txt` must exist, otherwise `FileNotFoundError`. Missing MO2 settings raise `ValueError` with the dump text at `_scanner.py:306-318`. Lines are read **in reverse**, and `+name` entries whose folder exists under the stage path are kept, followed by the overwrite folder. Each mod is walked top-down, pruning skip dirs case-insensitively and skipping files with a skip suffix. `folders`/`files` are keyed by mod-relative path (later mods win, so the highest priority wins). `modules`/`archives` hold only root-level files. ⚠ T-8 | `_scanner.py:303-373` |

### 7.3 Data checks (walk of `Data`, top-down)

The walk visits Data's root files first, under the pseudo-root `Data`, which has no whitelist. Each top-level folder then sets the "data root" (lowercased) for everything under it. A top-level folder not in `DATA_WHITELIST` (`scan_settings.py:39-64`: f4se, materials, meshes, music, textures, scripts, sound, vis) is **pruned entirely**. The mod name and path come from the index (SCN-S6) when present, otherwise `""` and the Data path.

| ID | Check (setting) | Rule | Problem |
|---|---|---|---|
| SCN-C1 | Junk Files | Top-level folder `fomod` → pruned | `Junk File`, `This is a junk folder not used by the game or mod managers.`, solution `It can either be deleted or ignored.` |
| SCN-C2 | Loose Previs | Top-level `vis` → pruned | `Loose Previs`, `_scanner.py:452`, ArchiveFolder |
| SCN-C3 | Loose Previs | `meshes/**/precombined` (any depth under meshes) → pruned | same as C2 |
| SCN-C4 | Problem Overrides | `meshes/**/animtextdata` → pruned | `Loose AnimTextData`, `The existence of unpacked AnimTextData may cause the game to crash.`, ArchiveFolder |
| — | (always) | Sub-folders in skip dirs are pruned (case-insensitive). Files with a skip suffix are skipped. | — |
| SCN-C5 | Junk Files | File name in `{thumbs.db, desktop.ini, .ds_store}` or ending `.tmp`/`.bak` → next file | `Junk File`, `This is a junk file not used by the game or mod managers.`, DeleteOrIgnoreFile |
| SCN-C6 | Problem Overrides | **Directly** in `Data/Scripts`, name in `F4SE_CRC` keys (`globals.py:332-362`), **and the file belongs to a mod in the index**. Without MO2 stage mode this never fires. CRCs are not compared. | `F4SE Script Override`, `_scanner.py:531-532` |
| — | — | Files without an extension skip the remaining checks | — |
| SCN-C7 | Wrong File Formats | The extension isn't in the data root's whitelist, **or** it's `.dll` and the relative folder doesn't start with `f4se\plugins` (case-insensitive string prefix, so it also fires for DLLs in Data's root). For `bmp jpeg jpg png psd tga` → `dds`, and `mp3` → `wav`/`xwm`, look for siblings with the proper extension: found → summary lists them and solution DeleteOrIgnoreFile; not found → ConvertDeleteOrIgnoreFile. Other extensions → UnknownFormat. | `Unexpected Format`, summary `Format not in whitelist for {root}.\n…` (`_scanner.py:553-559`) |
| SCN-C8 | Wrong File Formats | `.ba2` not in `ARCHIVE_NAME_WHITELIST` (`globals.py:157-197`), not in the enabled-archive set, and the stem has no ` - ` suffix or its suffix isn't a valid language suffix (INI-4) | `Invalid Archive Name`, `This is not a valid archive name and won't be loaded by the game.`, RenameArchive, extra `\nValid Suffixes: …` and `Example: {lowercased prefix} - Main.ba2` |
| SCN-C9 | Errors | **No check is implemented.** The box only enables the data walk. | — |

### 7.4 Results and details

| ID | Behaviour | Source |
|---|---|---|
| SCN-R1 | Info label `{n} Results ~ Select an item for details`. | `_scanner.py:250` |
| SCN-R2 | Overview problems with mod `OVERVIEW` get the mod name from the index by `relative_path` (just the file name, so this only matches mod-root files) when Overview Issues is ticked and an index exists. Otherwise they get `""`. This **mutates the shared problem objects**. | `_scanner.py:252-259` |
| SCN-R3 | One expanded group per distinct problem type. **Group order comes from Python set iteration** (string-hash randomised per process). 🐞 B-4. Inside a group, items are sorted by `type + mod`, stable. The item text is the path name (ProblemInfo) or the path string (SimpleProblemInfo). Stage mode adds the mod column. | `_scanner.py:261-285` |
| SCN-R4 | Details pane rows: `Mod:` (stage mode only, 12pt, value `mod or 'N/A'`), then `Problem:`, `Summary:`, `Solution:` (10pt). Values wrap at 560px. `Problem:` shows `relative_path`. | `_scanner.py:689-760,781-786` |
| SCN-R5 | The path label is clickable when the path is a `Path` and it or its parent exists. Clicking opens the folder (or the parent of a file) in Explorer, with tooltip `Click to open location`. Otherwise the click is unbound and the cursor becomes `X_cursor`. | `_scanner.py:788-803` |
| SCN-R6 | Solution text is `solution or 'No solution suggestion.'`, plus `\n` and the extra-data lines. If the first extra entry starts with `http`: left-click opens it, right-click copies it, tooltip `Left-Click: Open URL\nRight-Click: Copy URL`. | `_scanner.py:805-833` |
| SCN-R7 | Buttons, right column: `Copy Details` puts `[Mod: …\n]Problem: …\nSummary: …\nSolution: …\n` on the clipboard and shows `Copied!` (disabled) for 3 s. `File List` appears for SimpleProblems with a file list and opens a TreeWindow 400×500: the title is `Race Animation Subgraph Records` with the info text re-flowed (`_scanner.py:856`), or `Files` with no text; headers `Records` / ` Module`. | `_scanner.py:767-876` |
| SCN-R8 | **Auto-Fix** is wired up (button states `Auto-Fix` accent / `Fixing...` / `Fixed!` + check icon on the tree row / `Fix Failed`, and a results AboutWindow 500×300 `Auto-Fix Results`), but **`AUTO_FIXES` is empty**, so the button never appears. Port the empty registry; there are no autofixes to port. | `autofixes.py:42-70`, `_scanner.py:878-896` |

## 8. Tools, Settings and About tabs

| ID | Behaviour | Source |
|---|---|---|
| TOOL-1 | Three equal-width labelframe columns (title centred on top): **Toolkit Utilities** (`Downgrade Manager` → §9.3, `Archive Patcher` → §9.4), **Other CM Authors' Tools** (5 links), **Other Useful Tools** (6 links). The button texts include their deliberate `\n` and leading-space layout, and the URLs and info tooltips are in `_tools.py:78-148`. | `_tools.py` |
| TOOL-2 | Link buttons open the URL. Their hover tooltip is `View on Nexus Mods` / `View on GitHub` / `Open website`, picked by substring. Entries with a description get an info icon to the right whose tooltip is the description. | `_tools.py:42-75` |
| SETT-1 | Two labelframes. **Update Channel**: `All: GitHub & Nexus Mods`=both, `Early: GitHub`=github, `Stable: Nexus Mods`=nexus, `Never: Don't Check`=none, each radio with tooltip `TOOLTIP_UPDATE_SOURCE`. **Log Level**: `Debug`/`Info`/`Error`, each with tooltip `TOOLTIP_LOG_LEVEL`. `WARNING` is valid in the file but not offered; if it's set, no radio is selected. | `_settings.py` |
| SETT-2 | Clicking a radio saves immediately. **Neither change takes effect until the next launch**: the log level isn't re-applied and the update check only runs at startup. | `_settings.py:105-107` |
| ABOUT-1 | Left: `Collective Modding\nToolkit` (20pt, centred) above `icon-256.png`. Right: `v0.6.2-dev\n\nCreated by wxMichael for the\nCollective Modding Community\n#cm-toolkit on Discord\nModified by RowanSkie` (12pt, centred), then three link rows (Nexus logo, Discord logo, GitHub logo), each with a vertical separator and the buttons `Open Link`/`Copy Link` (Discord: `Open Invite`/`Copy Invite`), width 12. Copy shows `Copied!` for 3 s. | `_about.py` |

URLs (`globals.py:42-44`): Nexus `https://www.nexusmods.com/fallout4/mods/87907`, Discord `https://discord.gg/tktyEyYHZH`, GitHub `https://github.com/RowanSkie/Collective-Modding-Toolkit`.

## 9. Modal windows

### 9.1 Modal base

| ID | Behaviour |
|---|---|
| MOD-1 | Owned by the main window. Fixed size, not resizable, centred on the **screen**, transient to its parent, dark title bar, focused, with an **application-modal grab**. On close, the previous grab holder (for example a parent modal) regains the grab. Escape and WM_DELETE close it, unless `processing_data` is set. |
| MOD-2 | **AboutWindow** (`title`, `text`, w×h): the text is 10pt, left-justified, anchored to the top, wrapping at the window width, with a `Close` button. Space also closes it. ⚠ The Close button's `width=win_width // 2` is measured in **characters**; check the rendering in screenshots. |

### 9.2 TreeWindow

Optional text label (wrap 90% of the width), a tree with a `#0` column 65px wide and one stretch column, headers (`#0` centred, the others left-aligned), and a vertical scrollbar. Rows are `text=str(item[0])` and `value=item[1].name`, sorted by `item[0]` descending; an empty list shows `No items to display.`. No selection. Close button; Space closes it. ⚠ T-3 for the `str()` of float counts and versions.

### 9.3 Downgrader (`Downgrader`, 600×334)

| ID | Behaviour | Source |
|---|---|---|
| DG-1 | Version detection runs over 6 files with their own CRC tables (`downgrader.py:55-100`). **Game**: `Fallout4.exe`, `Fallout4Launcher.exe`, `steam_api64.dll`. **Creation Kit**: `CreationKit.exe`, `Tools\Archive2\Archive2.exe`, `Tools\Archive2\Archive2Interop.dll`. Each gets a full CRC32, mapped to an Install Type, `Unknown`, or `Not Found`. Its NG/OG/AE CRC sets (`CRCs_by_type`) are derived with NG/AE counting for both. ⚠ T-2: `InstallType.AE2` is an alias of `AE`. | `downgrader.py:101-113,137-152` |
| DG-2 | Layout: `Current Game` and `Current Creation Kit` labelframes (file name with a colon, right-aligned, plus a coloured type label); `Desired Version` radios `Old-Gen` / `Next-Gen` (default Old-Gen iff Fallout4.exe is Old-Gen); `Options` checkboxes `Keep Backups` / `Delete Patches` (from settings, with their tooltips); a big `Patch\n All` button; an `About` button (AboutWindow 500×300 `ABOUT_DOWNGRADING`); a log pane (§9.5) seeded with `💭 Patches will be downloaded and applied as-needed.`; and a progress bar. | `downgrader.py:154-229` |
| DG-3 | Label colours: AE `salmon`. Next-Gen & Anniversary → shown as `Anniversary`/salmon on an AE game, `Next-Gen`/SlateBlue1 on an NG game, otherwise unchanged in bad. NG `SlateBlue1`. Not Found `gray`. Unknown/Obsolete bad. Anything else `dodger blue`. | `downgrader.py:231-273` |
| DG-4 | Patch All: disable the button, clear the log, save the changed options (SET-7). Per file, in order: already the desired type → `Skipped X: Already Old-Gen.`; Not Found → `Skipped X: Not Found.`; AE or Obsolete → `Skipped X: Unsupported Version.`; otherwise patch it (DG-5). The button is re-enabled only if the **last** file didn't need patching. 🐞 B-11 | `downgrader.py:275-319` |
| DG-5 | Patch one file. The current CRC must be in the opposite type's set, otherwise `Skipped X: Unsupported Version.`. Clear the read-only attribute. Backup names are `<stem>_upgradeBackup<ext>` (holds OG) and `<stem>_downgradeBackup<ext>` (holds NG). If a backup of the *current* version exists: same CRC → delete the live file; different → delete the backup. Rename the live file to the current-version backup. If a *desired*-version backup exists with a valid CRC: copy it into place (Keep Backups) or move it, and log `✅ Patched X`; with an invalid CRC, delete it. If the live file still doesn't exist, queue a download of `…/delta-patches/{NG-to-OG-\|OG-to-NG-}<name>.xdelta`. If it does exist and Keep Backups is off, delete the current backup. Any `OSError` → `❌ Failed patching X`. | `downgrader.py:321-392` |
| DG-6 | Queue processing (sequential). The patch file name is the URL's last segment **in the CWD**: an existing file is reused without validation, otherwise it is downloaded on a worker thread (§11 NET-3) while progress is polled every 100 ms. Once the download is done, `xdelta3` decodes (current backup → live file) **on the UI thread**, logging `✅ Patched X` or `❌ Failed patching X`. Then the backup input is deleted (Keep Backups off; 🐞 B-9, even on failure) and the patch is deleted (Delete Patches on). When the queue is empty: refresh Overview, re-detect versions, redraw, re-enable the button. Closing is blocked while the queue runs. 🐞 B-10 | `downgrader.py:394-466` |

### 9.4 Archive Patcher (`Archive Patcher`, 700×600)

| ID | Behaviour | Source |
|---|---|---|
| AP-1 | Top: a `Desired Version` labelframe with radios `v1 (OG)` (default) / `v8 (NG)`. A filter caption in neutral-2: when the target is v1 it's `PATCHER_FILTER_NG` (`Showing all v7 & v8\n(Includes Base Game/DLC/CC)`), otherwise `PATCHER_FILTER_OG`. Buttons `Patch All` and `About` (AboutWindow 500×435, `ABOUT_ARCHIVES`). Middle: `Name Filter:` entry plus a file tree (names only, sorted by full path). Bottom: log pane. | `patcher/_base.py:70-105`, `_archives.py:67-111` |
| AP-2 | Candidates are the Overview's NG archives (target v1) or OG archives (target v8), i.e. **enabled** archives only, base game included. The name filter is a case-folded substring of the file name, re-applied on each key release; each refresh logs `💭 Showing N files to be patched.` (not written to the log file). Changing a radio clears the log, updates the caption and repopulates. | `_archives.py:60-65,105-111,182-185`, `_base.py:120-128` |
| AP-3 | Patch All runs **on the UI thread**. Nothing to do → `💭 Nothing to do!`. Per file (`r+b`): not `BTDX` → `❌ Unrecognized format: X`; byte 4 already equals the target → `❌ Skipping already-patched archive: X`; byte 4 not in the source set (target v1: {7, 8}; target v8: {1}) → `❌ Unrecognized version [hh]: X`; otherwise write the **single byte** at offset 4 (1 or 8) → `✅ Patched to v{1\|8}: X`. Errors: `Failed patching (File Not Found\|Permissions/In-Use\|Unknown OS Error): X`. Summary: `💭 Patching complete. {ok} Successful, {fail} Failed.`. Afterwards: refresh Overview and repopulate the tree. The read-only clear is **deliberately disabled** (MO2/Win11 issue 2174). | `_archives.py:113-180`, `_base.py:107-118` |

### 9.5 Log pane (`Logger`)

A Text widget 8 lines high, 10pt, word-wrapped, with a vertical scrollbar. Read-only: every key is blocked except Ctrl+A / Ctrl+C (`utils.py:205-209`, ⚠ T-13). Each message is `{emoji}{text}\n` with `❌ ` / `✅ ` / `💭 `. Only the emoji is coloured (bad / good / `dodger blue`), and the pane auto-scrolls to the end.

## 10. Update check

| ID | Behaviour | Source |
|---|---|---|
| UPD-1 | Runs once, **synchronously at startup** before the window appears, per `update_source`: `none` skips it, `nexus`/`github` checks one source, `both` checks Nexus then GitHub. | `cm_checker.py:109-118` |
| UPD-2 | When either source reports a newer version, a pale-green banner sits above the tabs: update icon plus `An update is available:` (dark green, 12pt), then `v{x} (NexusMods)` and/or `v{y} (GitHub)` (SteelBlue4, bold underline, hand cursor, tooltips `Open Nexus Mods`/`Open GitHub`), separated by ` / `. Clicking opens `NEXUS_LINK` / `GITHUB_LINK` (the RowanSkie repo). | `cm_checker.py:119-183` |
| UPD-3 | The comparison is PEP 440 `Version(latest) > Version("0.6.2-dev")`. ⚠ T-4 | `utils.py:381,409` |
| UPD-4 | Failures are logged and treated as no update. | |

## 11. Network calls

| ID | Call | Details |
|---|---|---|
| NET-1 | Nexus page | `GET https://www.nexusmods.com/fallout4/mods/87907`, `timeout=5` (per read, ⚠ T-14), streamed line by line. The line **after** the one whose left-stripped text starts with `<meta property="twitter:label1" content="Version"` is split with `rsplit('"', 2)`, and `[1]` is the version. Errors caught: `RequestException`, `InvalidVersion`, `IndexError`. A non-200 status means no update. |
| NET-2 | GitHub API | `GET https://api.github.com/repos/wxMichael/Collective-Modding-Toolkit/releases/latest`, headers `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`, plus the requests default `User-Agent: python-requests/x` (⚠ T-14). `tag_name` is compared. Note: this queries **wxMichael** while the banner links to **RowanSkie**. 🐞 B-16 |
| NET-3 | Delta patches | `GET https://github.com/wxMichael/Collective-Modding-Toolkit/releases/download/delta-patches/<dir><file>.xdelta`, `timeout=10` (per read), redirects followed, streamed in 1 KiB chunks to the CWD, progress `downloaded / content-length`. No status check (🐞 B-10). Hosting is an open decision: [Decide where the Downgrader's delta patches are hosted](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/16). |
| NET-4 | Browser | `webbrowser.open` for the About/Tools/banner/solution links. |

## 12. File system, registry and Win32 touch-points

**Reads.** `settings.json`, `cm-toolkit.log` (append), `assets/*`. `Documents\My Games\Fallout4\{Fallout4,Fallout4Prefs,Fallout4Custom}.ini`. `ModOrganizer.ini` and `portable.txt`. `profiles\<p>\modlist.txt`. The 7 Overview binaries and 6 Downgrader files: version resource plus full CRC32. `Data\F4SE\Plugins\version-*.bin` (exists). `Data\Fallout4 - Startup.ba2` (CRC32 from byte 12). `Fallout4.ccc`. `%LOCALAPPDATA%\Fallout4\plugins.txt`. BA2 headers (12 bytes). Module headers (34 bytes) and **whole modules** (Race Subgraphs). F4SE DLLs (loaded as images). Recursive walks of Data and every enabled MO2 mod. Sibling-format existence probes.

**Writes.** `settings.json`, `cm-toolkit.log`. Byte 4 of BA2 files. Downgrader: clearing the read-only attribute, rename/copy/move/delete of game and CK files, `*_upgradeBackup.*` / `*_downgradeBackup.*`, `*.xdelta` in the CWD, and xdelta3 output.

**Existence checks on Windows 11 24H2+ (build ≥ 26100).** `is_file`, `is_dir` and `exists` switch to open/iterdir probes to work around the MO2 VFS (`utils.py:90-146`, duplicated in `mod_manager_info.py:34-53`). Semantics: `is_file` succeeds when the open succeeds; on `PermissionError` it is true only if `iterdir` raises `NotADirectoryError`. `is_dir` is true when `iterdir` doesn't raise `NotADirectoryError`/`FileNotFoundError` (other errors propagate). `exists` is true on a successful open, otherwise it depends on iterdir. ⚠ T-12

**Registry.** `HKCU\Software\Mod Organizer Team\Mod Organizer\CurrentInstance`, `HKLM\SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4\Installed Path`, `HKLM\SOFTWARE\WOW6432Node\GOG.com\Games\1998527297\path`, the CPU and GPU keys (PC-4/5). Only `REG_SZ` (or `REG_QWORD` for VRAM) is accepted.

**Win32 / OS.** `AddFontResourceExW`, `SHGetFolderPathW` (CSIDL 5 and 28), `DwmSetWindowAttribute` 19/20, `GetFileVersionInfo`, `LoadLibraryExW(DONT_RESOLVE_DLL_REFERENCES)`, `ntdll!wine_get_version`, the parent-process chain, total physical memory, `os.startfile` (open a folder), clipboard.

## 13. Threads and `after()` polling

| ID | Pattern |
|---|---|
| THR-1 | The Scanner worker thread plus 100 ms queue polling (SCN-S3/S4). |
| THR-2 | The Downgrader download thread plus 100 ms progress polling. Decoding runs on the UI thread (DG-6). |
| THR-3 | `after(3000)` revert of the `Copied!` buttons (Copy Details, About tab copy buttons). |
| THR-4 | **Everything else blocks the UI thread**: the update check (up to 2 × 5 s before the window shows), Overview load/refresh (full CRC32 of ~7 binaries, including `Fallout4.exe`, plus the Startup BA2), F4SE DLL loading, Archive Patcher, Downgrader detection and xdelta decode. The port may move this work off-thread *only if* the observable results and ordering are unchanged; the decision belongs to [Decide Core domain seams and the Core-App contract](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/7). |

## 14. Dead and unused code (do not port as features)

`utils.rglob` (unused; its 24H2 branch could never match because it tests `endswith("*.ext")`), `utils.read_text_encoded`, and therefore **`chardet` is unused**. `ModManagerInfo.executables` (MO2-10). `profile_local_inis`/`profile_local_saves` (read, never used). `GameInfo.archives_gnrl`/`archives_dx10` (never filled; the counts use `ba2_count_*`). `ckfixes_found`. `is_foogng`. `DLLInfo.SupportsCurrent`. `RECORD_TYPES`. `ProblemType.MisplacedDLL`. `SolutionType.ArchiveOrDeleteFile` and `DeleteFile`. `Magic.DDS`. `CSIDL.Desktop`/`AppData`. `FONT_SMALLER`, `SEPARATOR_WIDTH`, `PADDING_SEPARATOR_*`. `TOOLTIP_SCAN_DDS`/`BA2`/`CONFLICTS`/`SUGGEST`. The empty `AUTO_FIXES` (SCN-R8). The Scanner `Errors` check (SCN-C9). The `InstallType` enum still needs `Obsolete2` for the AE-limit check (OVW-A5), even though nothing produces it.

## Parity traps

From [Map the remaining Python dependencies to .NET equivalents](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/4), plus traps found during this inventory.

| ID | Trap | Where |
|---|---|---|
| T-1 | **Text decoding.** Python `read_text("utf-8")` is strict (it raises on bad bytes), keeps a BOM as `U+FEFF` (so a BOM'd first line doesn't start with `[`), and `splitlines()` splits on `\r \n \r\n \v \f \x1c-\x1e \x85 \u2028 \u2029`. Use a strict UTF-8 decode over the bytes plus a Python-style line splitter. Behaviour on bad input: #18. `head[8:].decode('utf-8')` in OVW-A4 can raise too. | INI-3, MO2-4, OVW-M3/M4, SCN-S6 |
| T-2 | **`InstallType` aliases.** `AE2 = "Anniversary"` duplicates `AE`'s value, so in Python **`AE2` is `AE`** (an alias), while `Obsolete2 = "Obsolete"` is a distinct member that nothing produces. Display strings: `Obsolete (OGNG)`, `Old-Gen`, `Down-Grade`, `Next-Gen`, `Anniversary`, `Obsolete`, `Next-Gen & Anniversary`, `Unknown`, `Not Found`. #7 models this. | `enums.py:35-45` |
| T-3 | **Float formatting.** `str(float)` is the shortest round-trip form and always has a decimal point: `1.0`, `1.7`, `68.0`, `0.94`. C# `ToString()` gives `1`. It is used for HEDR matching (OVW-M7), problem text, and TreeWindow cells. | OVW-M7, §9.2 |
| T-4 | **Versions.** The update check needs a PEP 440 comparer (`v`-prefixed tags, `-dev`, `1.2 == 1.2.0`). A missing version resource is `None` in Python, while .NET returns `0.0.0.0`, so treat all-zero as missing. MO2's version is built from the first 3 file-version parts. | UPD-3, OVW-B2, MM-1 |
| T-5 | **Process and PE.** MM-1 must handle a normal and an elevated MO2 parent. `Process.ProcessName` drops `.exe`, and `MainModule` can fail across elevation. F4SE: Python's `LoadLibraryEx` raises `OSError` on non-x64 or broken DLLs (the tab fails), while a managed PE reader would succeed. Validate against real plugins. | MM-1, F4SE-3 |
| T-6 | **OS string.** `platform.release()` returns `"11"` on Win11 and OVW-5 compares the whole string literally. Build-number lookup and the trailing space need reproducing. | PC-2, OVW-5 |
| T-7 | **Path joins.** On Windows, `Path("D:/MO2") / "/mods"` gives `D:\mods`, not `D:\MO2\mods`, and `Path()` collapses doubled backslashes from `@ByteArray(C:\\Games\\…)`. `Path.Combine` behaves differently on both counts. | MO2-6 |
| T-8 | **Case-insensitive paths.** `PureWindowsPath` equality and hashing are case-insensitive, so the enabled-archive set, `p not in current_plugins`, and the mod-index dictionaries all match regardless of case. Use `StringComparer.OrdinalIgnoreCase`-style comparers. | OVW-A3, OVW-M5, SCN-S6, SCN-C8 |
| T-9 | **Python `str()` in UI text.** Bools render as `True`/`False`, lists as `['.mohidden']`, `Path` objects with backslashes. | OVW-4 |
| T-10 | **JSON output.** `json.dump(indent="\t")` writes one key per line, tab-indented, with `": "` after keys and no trailing commas. It escapes non-ASCII as `\uXXXX`, and the code appends a final newline. `System.Text.Json` defaults differ (2-space indent, and it escapes `+`, `<`, `'` etc.), so byte-identical output needs a custom writer. | SET-6 |
| T-11 | **Rounding.** `round()` is banker's rounding (`round(2.5) == 2`). Match it with `Math.Round(x, MidpointRounding.ToEven)`, which is .NET's default. | PC-3, PC-5, OVW-M7 |
| T-12 | **MO2 VFS.** Under MO2, the INIs, plugins.txt and Data contents are seen through usvfs hooks, and §12's 24H2 probe semantics exist because of them. The .NET file APIs must be checked under the VFS; this is the map's "MO2 VFS" fog. | INI-2, OVW-M4, §12 |
| T-13 | **Ctrl detection.** `event.state == 12` is an *exact* match on Tk's modifier mask (Control `0x4` plus `0x8`, which Tk on Windows is believed to use for a lock key such as NumLock). Ctrl+A/C may therefore work only in particular lock-key states. Verify empirically during the log-pane slice, and file a bug if so. | §9.5 |
| T-14 | **HTTP.** Python's `timeout=` is a per-read timeout, while `HttpClient.Timeout` is a total, so a plain timeout would kill large Downgrader downloads. GitHub's API needs a User-Agent, which `HttpClient` doesn't send by default. | NET-1..3 |
| T-15 | **Set and dict ordering.** Dicts keep insertion order (port as ordered). String **sets** iterate in hash-randomised order: the SCN-R3 group order and the enabled-archive iteration order. The latter only affects the order of archive problems within the Overview list. | SCN-R3, OVW-A4 |

## Suspected bugs

Each is reproduced as-is in the port and tracked as a follow-up `bug` issue (linked in the ID column). B-9, B-10 and B-14 were already filed during earlier research tickets, and the inventory's extra findings were added to them as comments.

| ID | Bug | Where |
|---|---|---|
| B-1 · [#20](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/20) | The Scanner's checkboxes always start ticked; the saved `scanner_*` settings are never read. | SET-8, SCN-3 |
| B-2 · [#21](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/21) | The scan worker clears `thread_scan` *before* enqueuing its results, so the UI can populate first and drop the data-scan results. | SCN-S4 |
| B-3 · [#22](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/22) | A scan-worker exception (for example a missing `modlist.txt` or MO2 settings) leaves the scan stuck on "Scanning..." with the button disabled. | SCN-S4, SCN-S6 |
| B-4 · [#23](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/23) | Result group order is random per run (set iteration). | SCN-R3 |
| B-5 · [#24](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/24) | MO2 default `skip_file_suffixes` / `skip_directories` are corrupted by CSV-parsing `str(tuple)` / `str(set)`, so `.mohidden` files aren't skipped unless the INI sets the key. Verified: they parse to `["('.mohidden'", ')']` and `['set()']`. | MO2-7 |
| B-6 · [#25](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/25) | *(Suspected; confirm that MO2 writes this form.)* `%BASE_DIR%/mods` resolves to `<drive>:\mods`. | MO2-6 |
| B-7 · [#26](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/26) | `"1.70"` in `MODULE_VERSION_SUPPORT` can never match, because `str(round(1.7, 2))` is `"1.7"`. | OVW-M7 |
| B-8 · [#27](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/27) | The MO2 Win11 warning matches only `Windows 11 24H2` exactly, not 25H2+, even though its tooltip says "24H2+". | OVW-5 |
| B-9 · [#14](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/14) | Downgrader: a failed xdelta decode still deletes the backup input when Keep Backups is off, which can leave the game without the file. | DG-6 |
| B-10 · [#15](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/15) | Downgrader: download failures (HTTP error body saved as `.xdelta`, network error, no `content-length` → `ZeroDivisionError`) kill the thread without signalling. Polling then runs forever and the window can't be closed. A cached `.xdelta` is reused without validation. | DG-6, NET-3 |
| B-11 · [#28](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/28) | Downgrader: the Patch button's re-enable depends only on the **last** file. It can stay disabled forever (for example an unknown-CRC `Archive2Interop.dll`) or re-enable during downloads. | DG-4 |
| B-12 · [#29](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/29) | Escape on the main window destroys it without the close guard, even while a scan thread is running. | SHELL-10 |
| B-13 · [#30](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/30) | `Unknown` binaries render green and raise no `Wrong Version` problem when the game itself is `Unknown`, because of the `case game.install_type` match. | OVW-B7 |
| B-14 · [#17](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/17) | Game INI parsing doesn't trim keys/values (`bNVFlexEnable = 1` isn't seen), a BOM breaks the first section, and non-UTF-8 INIs are fatal. | INI-3 |
| B-15 · [#31](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/31) | A missing `[Archive]` section (for example `Fallout4.ini` never generated) raises and fails the Overview tab. | OVW-A2 |
| B-16 · [#32](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/32) | The update check queries the **wxMichael** releases while the app version and banner link are the **RowanSkie** fork. | NET-2 |
| B-17 · [#33](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/33) | *(Suspected; confirm with a real MO2 `plugins.txt`.)* CC modules listed in both `Fallout4.ccc` and `plugins.txt` are counted twice. | OVW-M4 |
| B-18 · [#34](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/34) | One unloadable DLL in `F4SE/Plugins` fails the whole F4SE tab ("Scanning DLLs..." stays forever). | F4SE-3 |

Minor quirks to keep, with no issue filed. A module missing the HEDR tag is unreadable but produces no problem entry (OVW-M6). Non-F4SE DLL rows leave `Your Game` blank (F4SE-4). The `msdia` prefix check is case-sensitive (F4SE-2). The Address Library name falls back to the CRC when `Fallout4.exe` has no version resource (OVW-B5). WARNING log level has no radio (SETT-1).
