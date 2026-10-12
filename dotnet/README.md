# Collective Modding Toolkit: C# / Avalonia port

The C# port of the app, built as a sibling tree next to the Python **Reference Implementation** in `src/`
([ADR-0001](../docs/adr/0001-port-to-csharp-avalonia.md)). It targets Behaviour Parity with the Parity Baseline and
ships as a NativeAOT `win-x64` exe ([ADR-0002](../docs/adr/0002-nativeaot-in-upstream-archive-layout.md)).

## Layout

| Path | What it is |
|---|---|
| `CMToolkit.slnx` | The solution. |
| `Directory.Build.props` | Shared settings: .NET 10, the single `<Version>`, warnings-as-errors (trim/AOT included), `<CETCompat>false</CETCompat>`. |
| `src/CMToolkit.Core` | The app's logic. No Avalonia reference; trim/AOT analyzers on (`IsAotCompatible`). |
| `src/CMToolkit.App` | The Avalonia 12.1 app (`cm-toolkit.exe`), with CommunityToolkit.Mvvm. `Startup/` is the startup sequence, `Tabs/MainTabs.cs` composes the tabs, and `Parity/` lets the exe run on a Parity Scenario's machine (screenshots, smoke test). |
| `tests/CMToolkit.Tests` | xUnit v3 and Avalonia.Headless tests. Tests that prove a Parity Inventory ID carry `[Trait("Parity", "<ID>")]`. `Parity/` holds the harness: the Parity Scenario source, the fake host and HTTP handler, and the golden comparer. |
| `tools/CMToolkit.ParityCoverage` | The Parity Inventory coverage check, run in CI (see [`parity/README.md`](../parity/README.md)). |
| `eng/` | CI scripts: the PE-not-CET-compatible check and the published-exe smoke test. |

## Build, test and publish

```sh
cd dotnet
dotnet build CMToolkit.slnx
dotnet test CMToolkit.slnx
dotnet publish src/CMToolkit.App -c Release -r win-x64 -o artifacts/publish
pwsh eng/Assert-NotCetCompatible.ps1 artifacts/publish/cm-toolkit.exe
pwsh eng/Test-ShellStarts.ps1 -Exe artifacts/publish/cm-toolkit.exe -Scenario ../parity/scenarios/shell/main-window
pwsh eng/Test-ShellStarts.ps1 -Exe artifacts/publish/cm-toolkit.exe
dotnet run --project tools/CMToolkit.ParityCoverage
```

Startup reads the machine before the main window appears (PC info, mod manager, game path, game INIs; SHELL-5). The
first smoke run uses a Parity Scenario's fake machine, so the window appears anywhere. The second uses the real one: it
passes with the main window where Fallout 4 is installed, or with the `Fallout 4 Not Found` question where it isn't.

The NativeAOT publish needs the Visual Studio C++ build tools. Its link step finds `link.exe` through `vswhere.exe`,
which must be on `PATH`. It lives in `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`, and that folder isn't on
`PATH` by default.

CI (`.github/workflows/dotnet.yml`) runs all of the above on Windows for pull requests and pushes to `main`.

## Published output

A NativeAOT publish (Avalonia 12.1.3, SkiaSharp and HarfBuzzSharp from its dependencies) produces:

| File | Ships in the release archive | Notes |
|---|---|---|
| `cm-toolkit.exe` | yes | ~19 MB. Embeds the manifest, icon, images and Cascadia Mono. |
| `cm-toolkit.pdb` | yes | The exe's symbols (ADR-0002). |
| `av_libglesv2.dll` | yes | Avalonia's ANGLE build (OpenGL ES over Direct3D). |
| `libHarfBuzzSharp.dll` | yes | Text shaping. |
| `libSkiaSharp.dll` | yes | Rendering. |
| `assets\download-source.txt` | yes | The Download Source. The repo copy says `github`; release packaging writes one archive per source. |
| `CMToolkit.Core.pdb` | no | Core is compiled into the exe; its symbols are already in `cm-toolkit.pdb`. |
| `libHarfBuzzSharp.pdb`, `libSkiaSharp.pdb` | no | Symbols for the native libraries, over 100 MB together. |

These are the **Avalonia native DLLs the release archive needs**: `av_libglesv2.dll`, `libHarfBuzzSharp.dll` and
`libSkiaSharp.dll`. The release slice packages them with `LICENSE.md` and `THIRD-PARTY-NOTICES.txt` under
`Tools\CM-Toolkit\`.

## Known residuals

- The window opens with Avalonia's `CenterScreen`, which centres the outer frame in the work area. The Reference
  Implementation offsets the client size from the full screen, so it sits ~20 px lower and ~8 px further right (at
  100 %, 1440p). Accepted rather than reproduced.
- The tab strip renders 1 px taller than Tk's, and tab text sits 1 px higher (see [`parity/screenshots/shell/`](../parity/screenshots/shell/)). The
  main-window-shell prototype has the same offset, so it predates this tree. Horizontal positions match exactly.
- `cm-toolkit.log` is written in the ANSI code page with `backslashreplace`, as Python wrote it, but through Windows'
  code-page tables: under cp1252, U+0081, U+008D, U+008F, U+0090 and U+009D are written as bytes where Python's codec
  escapes them. A traceback is .NET's `Exception.ToString()`, not Python's (not part of Behaviour Parity).
- `settings.json` nested more than 1000 arrays/objects deep is treated as unparseable (reset); Python's limit is its
  C-stack guard, which depends on the machine.
- MO2's `skip_*` values are lowercased with `ToLowerInvariant`. Python's `str.lower()` differs for U+0130 (`İ` becomes
  two characters) and a word-final capital sigma (`ς`); no scenario covers them.
- `platform.release()` and the build come from `RtlGetVersion` (version and product type), where Python asks WMI. They
  report the same numbers; a machine whose WMI is broken makes Python fall back to `ver`, which isn't reproduced.
- A tab page is 754x404 at (3, 43), where Tk's is 758x407 at (1, 42) (the notebook theme's frame), so tab content
  sits 2 px right and 1 px lower, and anything anchored to the page's right or bottom edge 2 px further left or up.
  On the Tools tab the 4 px narrower page also leaves the columns 1-2 px narrower. See
  [`parity/screenshots/tools/`](../parity/screenshots/tools/), `settings/` and `about/`.
- The About tab's 20 pt title measures 289 px where Tk's is 288, so it sits half a pixel off centre. The Settings
  tab's Log Level frame is 86 px wide where Tk's is 88: Tk keeps 8 px either side of a labelframe's title where the
  GroupBox theme keeps 7.
- A link that the shell can't open (no handler for its scheme) does nothing. Python's `webbrowser.open` would then
  try the browsers it found on PATH and Edge's install folder; the reference's links are all `https://`, which
  Windows always has a handler for.
- The startup file picker (GP-2) is the native one Avalonia opens, owned by a window that is created but never shown,
  where Tk's was owned by its withdrawn root. The yes/no and error boxes are the app's sv-styled message boxes (issue
  #9), centred on the screen, since no main window exists yet.
- Game INI section names and keys are lowercased with `ToLowerInvariant`, with the same `str.lower()` differences as
  MO2's `skip_*` values above.
- The update check (#53) sends `User-Agent: cm-toolkit/<version>` and `Accept: */*`, where requests sent
  `python-requests/2.32.5` plus its other session defaults; GitHub needs some User-Agent. urllib3's own DEBUG lines
  ("Starting new HTTPS connection ...") that reached `cm-toolkit.log` at `log_level` DEBUG are not written.
  Response charsets other than the UTF-8/16/32, Latin-1 and ASCII codecs count as unknown (Python's `LookupError`,
  which fails startup, bug #81), and a GitHub body with no usable encoding is decoded as UTF-8 where requests asks
  chardet. Real chunk boundaries also differ:
  the port always reads the Nexus page in 512-byte chunks (as the parity harness does), while urllib3 follows the
  server's chunked encoding, so bug #82 strikes on different pages. HttpClient does not follow an https-to-http
  redirect, which requests did.
- The update banner matches Tk to the pixel at 100 % (see [`parity/screenshots/update-banner/`](../parity/screenshots/update-banner/));
  its link tooltips are Avalonia's, not tktooltip's.
- Overview (see [`parity/screenshots/overview/`](../parity/screenshots/overview/), rechecked with all three frames
  built): the frames now match Tk's widths, and the Archives and Modules content lines up to the pixel. Tk grids an
  unpadded labelframe's content from its border line, so the Overview's GroupBoxes give back the theme's 4 px side
  gutters. The Binaries names column still sits ~2 px right of Tk's (a ttk label's internal padding around its text).
  The Downgrade Manager, Archive Patcher and refresh buttons' paddings were measured off the pairs rather than derived
  from sv_ttk.
- If an Overview load or refresh fails part-way (a missing `[Archive]` section, B-15; text that isn't UTF-8, T-1), the
  error is reported and the report becomes what had been gathered, as the reference's shared state was left; a step
  that never started keeps the previous report's part. The frames stay as they were. A failed load's warning boxes
  appear after the Error Window rather than before the failure (error surfacing is outside parity, #18).
- The Overview's enabled archives, like every Python set it keeps, are checked in insertion order, where Python's
  order is the string hash's; only the order of the Invalid Archive problems and `Archives: Checking` log lines
  depends on it (T-15), and the goldens compare those as unordered.
- Downgrader (see [`parity/screenshots/downgrader/`](../parity/screenshots/downgrader/)): the frames, radios, checkboxes,
  buttons and the log pane's box line up with Tk's. The log pane's emoji is Segoe UI Symbol's filled glyph in the
  tag colour, where Tk's GDI fallback draws Segoe UI Emoji's outline, so the message starts ~4 px further left; its
  scrollbar is Fluent's, which hides its thumb with nothing to scroll where sv_ttk's shows a full-height one. The
  progress bar is below the fixed 334 px window in both (clipped, as in Tk), so its look isn't compared. The log
  pane's key handling (Ctrl+A/C only, T-13) is the Archive Patcher slice's.
