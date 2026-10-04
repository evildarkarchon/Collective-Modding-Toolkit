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
| `src/CMToolkit.App` | The Avalonia 12.1 app (`cm-toolkit.exe`), with CommunityToolkit.Mvvm. |
| `tests/CMToolkit.Tests` | xUnit v3 and Avalonia.Headless tests. Tests that prove a Parity Inventory ID carry `[Trait("Parity", "<ID>")]`. |
| `eng/` | CI scripts: the PE-not-CET-compatible check and the published-exe smoke test. |
| `docs/screenshots/` | Hand-captured screenshot pairs against the Tk reference, at 100 % DPI. |

## Build, test and publish

```sh
cd dotnet
dotnet build CMToolkit.slnx
dotnet test CMToolkit.slnx
dotnet publish src/CMToolkit.App -c Release -r win-x64 -o artifacts/publish
pwsh eng/Assert-NotCetCompatible.ps1 artifacts/publish/cm-toolkit.exe
pwsh eng/Test-ShellStarts.ps1 -Exe artifacts/publish/cm-toolkit.exe
```

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

- The tab strip renders 1 px taller than Tk's, and tab text sits 1 px higher (see `docs/screenshots/shell/`). The
  main-window-shell prototype has the same offset, so it predates this tree. Horizontal positions match exactly.
