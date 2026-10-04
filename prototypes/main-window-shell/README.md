# PROTOTYPE: main window shell + Overview tab (throwaway)

> Throwaway code. It lives only on the `prototype/main-window-shell` branch and must not be merged or built on.
> It answers one question for the wayfinder ticket
> [Prototype the main window shell and Overview tab](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/9)
> (map: [Port CMT to C# / Avalonia](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/2)).

**Question:** what should the Avalonia main window shell and Overview tab look like to read as "the same app"
next to the Python original, and which styling approach should the real build use?

## Run it

```sh
cd prototypes/main-window-shell
dotnet run                                   # variant C, baseline data
dotnet run -- --variant A --scenario problems
```

A magenta-framed **PROTOTYPE controls** window opens below the main window. It's deliberately not part of the
design. Use it to switch:

| Control | What it compares |
|---|---|
| Variant ◀ ▶ (also ←/→ in the main window) | **A** palette + Fluent resource keys only · **B** A + selector `Styles` reaching into Fluent's template parts · **C** A + our own `ControlTheme`s for TabControl/TabItem/GroupBox |
| Data | `baseline` = the exact values in `python-overview.png`; `problems` = MO2, update banner, every icon and colour |
| Message box | custom sv-styled `Window` · Win32 `MessageBoxW` (what tkinter uses today) · MessageBox.Avalonia 12.0.0 |
| Log / emoji | Downgrader-style log, default font fallback vs. forced Segoe UI Symbol |
| TreeWindow | core `TableView` (Avalonia 12.1) restyled to ttk.Treeview metrics |
| DWM dark-title call | whether `utils.set_titlebar_style`'s P/Invoke is still needed |

Hover a version label in the Binaries box to see the Install Type → file version swap.

## Reference and evidence

- `python-overview.png`, `python-downgrader.png`: the Reference Implementation at the Parity Baseline, run on
  Tk 8.6 (python.org 3.14, like the shipped build), captured with `shot.ps1` (PrintWindow) at 100 % / 96 DPI.
- `shots/compare-*.png`: `[python | avalonia | 50/50 blend]`, built by `compose.py`. Crisp text in the blend
  means alignment; doubled text means drift.
- `shots/avalonia-*.png`, `shots/dialog-*.png`: raw captures. Regenerate with `capture.ps1`,
  `capture-dialogs.ps1` and `capture-tooltip.ps1` after `dotnet build`.

## What it showed

1. **Window:** `Width=760 Height=450 CanResize=False` gives client 760×450 and outer 776×489, the same as Tk.
   Per-monitor-v2 aware.
2. **Dark title bar** comes from `RequestedThemeVariant="Dark"` alone on Windows 11. The DWM P/Invoke isn't
   needed there. Windows 10 is untested.
3. **Styling approach:** palette + resource keys get everything except the notebook. Fluent tabs are pivot-style
   with an accent "pipe" and no strip (A). Reshaping them with selector Styles (B) only works if you copy Fluent's
   own pseudo-class selectors, and it can't change GroupBox's hard-coded 6 px gutter. Own `ControlTheme`s for
   **TabControl, TabItem and GroupBox** (C, ~130 lines of XAML) reproduce the notebook and Labelframe
   measurements. Button, ScrollBar, ToolTip, CheckBox and TableView stay on Fluent templates, reached by
   resource keys.
4. **Text metrics:** this mattered most for "same app".
   - *Line pitch:* Tk/GDI spaces Cascadia Mono lines by `usWinAscent+usWinDescent` (21 px at 16 px). Avalonia
     uses typo/hhea metrics (18.6 px), so every stacked-label block came out ~13 % shorter. Fix: `LineHeight`
     per size, 11→15, 13→17, 16→21, 27→35.
   - *Advance widths:* GDI hinting rounds Cascadia's advance to whole pixels (9.375→9 at 16 px, 7.617→8 at
     13 px), so Tk text is 4 % narrower at 16 px and 5 % wider at 13 px. Fix: `LetterSpacing` = rounded −
     exact (−0.375 / +0.383 / −0.445 / +0.18).
   - With both fixes the 50/50 blend is crisp across the header block, Archives, Modules, tabs and buttons.
   - The static Cascadia Mono 2407.24 TTFs have the same metrics as the bundled 2404.023 variable font.
   - *Not fixable:* Skia's rasterizer doesn't snap stems like GDI, so text has ~34 % more lit pixels and
     looks slightly heavier. Subpixel (the default) and grayscale both look this way. This is an accepted
     non-pixel-identical difference.
5. **`:is(TextBlock)`, not `TextBlock`:** string content in Button/TabItem renders through `AccessText`, and a
   plain type selector misses subclasses.
6. **Tk `pack(side=LEFT, expand=True)`** gives each box its natural width plus an equal share of the slack.
   Grid star columns don't do this, so the three Overview boxes need a ~40-line `PackRow` panel.
7. **Emoji:** Avalonia's default fallback already draws ❌ ✅ 💭 as monochrome glyphs tinted by the run
   foreground, the same as Tk 8.6. No colour-emoji problem and no special handling needed.
8. **TableView:** usable for TreeWindow and F4SE once `TableViewRowPadding` is cut from 42 px rows to ttk's
   ~22 px and the header weight is set to Normal. Its vertical scrollbar didn't appear with
   `VerticalScrollBarVisibility=Visible` (3 rows), so this needs a look in the F4SE work.
9. **Message boxes:** MessageBoxW = exact parity (light native box, blocking, system-DPI-aware even in a PMv2
   process). Custom dialog = on-theme, async. MessageBox.Avalonia = its "Warning" icon looks like an error, and
   it pulls in a *nightly prerelease* `DialogHost.Avalonia`.
10. **NativeAOT:** `dotnet publish -r win-x64 -p:PublishAot=true` builds with zero warnings once XAML
    includes aren't URI-loaded at runtime. `new StyleInclude(uri)` throws "No precompiled XAML found" under
    AOT. The exe is 19 MB plus `libSkiaSharp`, `libHarfBuzzSharp` and `av_libglesv2`. All variants render
    pixel-identical to the JIT build, and every dialog opens. The link step needs `vswhere.exe` on PATH.

Known residual: the Binaries box content sits ~8 px right of Tk's. Tk's `grid` overflows/centres that
frame's content. It's sub-structural, so it was left.
