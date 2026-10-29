# Collective Modding Toolkit: C# / Avalonia app

The app. It started as a C# port of the Python app ([ADR-0001](../docs/adr/0001-port-to-csharp-avalonia.md)), which
it replaced at 0.7.0; the Python app is preserved at the `python-reference` tag. It ships as a NativeAOT `win-x64` exe
([ADR-0002](../docs/adr/0002-nativeaot-in-upstream-archive-layout.md)).

## Layout

| Path | What it is |
|---|---|
| `CMToolkit.slnx` | The solution. |
| `Directory.Build.props` | Shared settings: .NET 10, the single `<Version>`, warnings-as-errors (trim/AOT included), `<CETCompat>false</CETCompat>`. |
| `src/CMToolkit.Core` | The app's logic. No Avalonia reference; trim/AOT analyzers on (`IsAotCompatible`). |
| `src/CMToolkit.App` | The Avalonia 12.1 app (`cm-toolkit.exe`), with CommunityToolkit.Mvvm. `Startup/` is the startup sequence, `Tabs/MainTabs.cs` composes the tabs, and `Parity/` lets the exe run on a Parity Scenario's fake machine (screenshots, smoke test): `ScenarioHostEnvironment` and `ScenarioHttpHandler`, the exe's own copies of the test harness's fake host and HTTP handler. |
| `tests/CMToolkit.Tests` | xUnit v3 and Avalonia.Headless tests. `Parity/` holds the scenario harness: the Parity Scenario source, the fake host and HTTP handler, and the golden comparer (see [`parity/README.md`](../parity/README.md)). |
| `eng/` | CI scripts: the PE-not-CET-compatible check and the published-exe smoke test. `Test-ScannerPanes.ps1` is a manual desktop check of the Scanner's docked panes (not run in CI). `generate-casefold.py` regenerates `PythonCaseFold.Table.cs` from Python's Unicode tables (run by hand when its Unicode version changes). |

## Build, test and publish

```sh
cd dotnet
dotnet build CMToolkit.slnx
dotnet test CMToolkit.slnx
dotnet publish src/CMToolkit.App -c Release -r win-x64 -o artifacts/publish
pwsh eng/Assert-NotCetCompatible.ps1 artifacts/publish/cm-toolkit.exe
pwsh eng/Test-ShellStarts.ps1 -Exe artifacts/publish/cm-toolkit.exe -Scenario ../parity/scenarios/shell/main-window
pwsh eng/Test-ShellStarts.ps1 -Exe artifacts/publish/cm-toolkit.exe
```

Startup reads the machine before the main window appears (PC info, mod manager, game path, game INIs). The first
smoke run uses a Parity Scenario's fake machine, so the window appears anywhere. The second uses the real one: it
passes with the main window where Fallout 4 is installed, or with the `Fallout 4 Not Found` question where it isn't.
It runs a copy of the publish folder, because the app writes `settings.json` and `cm-toolkit.log` beside its exe.

The NativeAOT publish needs the Visual Studio C++ build tools. Its link step finds `link.exe` through `vswhere.exe`,
which must be on `PATH`. It lives in `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`, and that folder isn't on
`PATH` by default.

CI (`.github/workflows/dotnet.yml`) runs all of the above on Windows for pull requests and pushes to `main`.

The Scanner's side and details panes are borderless windows owned by the main window (`Views/OwnedPaneDocker`).
`pwsh eng/Test-ScannerPanes.ps1 -Exe artifacts/publish/cm-toolkit.exe [-Drag]` walks them through moves, a second
monitor, minimise/restore, tab switches and a rescan on a real desktop (`-Drag` drags the title bar with the real
mouse). It has passed at 100 % on two 100 % monitors, but not yet at 150 % or with monitors at different scales.

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
`libSkiaSharp.dll`. A release (built and published by hand) packages them with `LICENSE.md` and `THIRD-PARTY-NOTICES.txt` under
`Tools\CM-Toolkit\`.

At run time the app keeps `settings.json`, `cm-toolkit.log` and the Downgrader's downloaded `*.xdelta` patches in this
same folder, beside the exe, whatever the working directory. The Python app resolved all three against the working
directory, so a launch from a shortcut, a terminal or an MO2 entry with another "Start in" scattered them
([#36](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/36)). At startup the app checks that it can
write to this folder by creating and deleting a probe file. If it can't, as for an install under a protected folder
such as `Program Files`, it keeps all three in `%LOCALAPPDATA%\CM-Toolkit` instead and logs a warning naming both
folders. Startup fails only if that folder can't be created or the log can't be opened there. If the app folder has
no `settings.json` but the working directory has one, where the Python app kept it, startup copies that file over
once and logs the copy. Only the settings move: the log and patches are disposable. Scenario runs
(`CMT_PARITY_SCENARIO`) use the scenario's working directory and copy nothing.
