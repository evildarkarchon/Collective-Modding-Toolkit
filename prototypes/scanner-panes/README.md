# PROTOTYPE: Scanner docked side and details panes (throwaway)

> Throwaway code. It lives only on the `prototype/scanner-panes` branch and must not be merged or built on.
> It answers one question for the wayfinder ticket
> [Prototype the Scanner's docked side and details panes](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/12)
> (map: [Port CMT to C# / Avalonia](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/2)).

**Question:** can the Scanner tab's docked panes be reproduced in Avalonia? If separate windows don't work, what's
the faithful alternative?

In Python, the panes are borderless `Toplevel`s (`wm_overrideredirect`) glued to the root's client edges:
`tabs/_scanner.py` `SidePane`/`ResultDetailsPane.update_geometry`, re-run on the root's `<Configure>`, plus
`cm_checker.py` `on_minimize/on_restore`.

## Run it

```sh
cd prototypes/scanner-panes
dotnet run                                       # variant A
dotnet run -- --variant B --pick "Race Subgraph"  # B, with a result selected so the details pane is open
dotnet run -- --variant A --selftest out.log      # scripted move/monitor/minimise/tab/rescan walk, then exit
```

A magenta **PROTOTYPE controls** window opens left of the main window. It is not part of the design.

| Control | What it does |
|---|---|
| Variant A / B / C | **A** borderless owned `Window`s at the Tk offsets · **B** the main window grows (+200 right, +200 down) and draws the panes inside · **C** fixed 760×450, panes become a column and a strip inside the tab |
| Select | Picks a result: one-line text, File List button, multi-line solution, Auto-Fix button |
| Lifecycle | Scan Game (destroys the details pane, fake 1.4 s scan), tab switches (destroy/recreate the panes like `switch_from`/`_switch_to`) |
| Move main | +137/+61, to the other monitor, straddling two monitors (side pane spills over), minimise for 2 s |
| Readout | Expected rect (the Tk formulas from the client origin, in physical px) vs `GetWindowRect`, per-pane DPI, `IsWindowVisible`, OK/DRIFT |

## Reference and evidence

- `reference/python-scanner-*.png`: the Reference Implementation at the Parity Baseline, Tk 8.6 (python.org 3.14,
  like the shipped build), running a **real scan** of this machine's install. Driven by `reference/drive_python.py`,
  captured by `reference/capture-python.ps1`. The uv-managed CPython ships Tk 9, which sv_ttk rejects; see the
  script header for the scratch venv.
- All captures are run through `mask.py`, which blacks out everything outside the app's own windows (screen grabs
  pick up whatever is behind them).
- `shots/avalonia-<variant>-<pick>.png`: the same screen region with the client origin in the same place
  (`capture.ps1`). `shots/compare-*.png` = `[python | A | B | C]` (`compose.py`).
- `drag.ps1`: drags a window by its title bar with **real mouse input** (the OS modal move loop) and samples all three
  window rects mid-drag. Works on both apps. Mid-drag frames: `shots/drag-avalonia-A.png`, `reference/drag-python.png`.

## What it showed (100 %, two 2560×1440 monitors)

1. **Python geometry, measured:** side pane 200×405 at client `(760, 40)`; details pane 760×200 at client
   `(0, 450)`, which overlaps the main window's 1 px bottom border. Both follow a move.
2. **Variant A works.** Borderless owned windows (`WindowDecorations=None`, `ShowInTaskbar=False`, `Show(owner)`)
   placed from `PointToScreen(0,0)` match the Tk formulas to the pixel in every self-test step: initial, move, other
   monitor, straddling monitors, minimise, restore, tab switch, rescan.
3. **Real title-bar drag:** 40 samples during a real mouse drag show **0 px** off the anchor for both panes. The
   Python reference gives the same 0 px over the same drag. Avalonia raises `PositionChanged` inside the modal move
   loop, so the panes move with the window.
4. **Minimise/restore needs no code.** Win32 hides owned windows when their owner is minimised and re-shows them on
   restore (`IsWindowVisible` false/true in the self-test). That replaces `CMChecker.on_minimize/on_restore`.
   Owned windows also always stay above their owner, which replaces the `tkraise` focus juggling
   (`ScannerTab.on_focus`, `SidePane.on_focus`, `ResultDetailsPane.on_focus`).
5. **Variant B** looks the same inside, but the title bar and frame now wrap the whole 960×650 composite, and the
   window changes size when the Scanner tab opens and when a row is first selected. It is one window, so there's
   nothing to track.
6. **Variant C doesn't fit.** At 760×450 the side pane clips three of the seven checkboxes and the tree drops to
   about 8 rows. It would need a redesign, so it isn't "structurally faithful".
7. **Details-pane text:** with the shell prototype's `LineHeight`/`LetterSpacing` pins, `wraplength=560` wraps at
   the same words as Tk ("…may result in stutter / when loading Cells."). Rows are 27 px apart, as in Tk.
8. **Side-pane bits:** the centred Labelframe header needs its own `CentredGroupBox` ControlTheme. Fluent's
   `BorderGapMaskConverter` only cuts the gap at a fixed left offset, so the header paints `SvBg` over the line.
   Tk's `relief=GROOVE` (#555555/#8E8E8E, swapped on the bottom/right) needs a ~30-line `GrooveBorder`, because
   `Border` has one brush for all four sides. Tree selection is `TreeViewItemBackgroundSelected=#292929`, not
   Fluent's accent.

## 150 % and mixed DPI (DISPLAY1 temporarily at 150 %, DISPLAY2 at 100 %)

9. **At 150 %** the panes come out at 300×608 and 1140×300 physical, exactly 1.5× the Tk sizes, anchored on
   the scaled client origin. The self-test reads OK at every step.
10. **Straddling** (owner at 100 %, side pane mostly on the 150 % monitor): Windows gives the pane its monitor's
    DPI (144). Converting the physical rect back to DIPs with the **pane's own** `DesktopScaling` keeps it at
    exactly 200×405 physical. But its content then lays out in only 133×270 DIPs and renders 1.5× too big:
    `shots/mixed-dpi-A-straddle.png` shows "Scan Settings" overflowing and checkboxes clipped. **Fix:** wrap the
    pane content in a `LayoutTransformControl` scaled by owner/pane scaling (`PaneWindow.MatchOwnerScale`). The
    pane then looks identical to the 100 % case (`shots/mixed-dpi-A-straddle-fixed.png`), and text stays sharp
    because it is still rasterised at the pane's density.
11. **Real drag across the DPI boundary:** 80 samples, **0 px** off in position and size. The owner itself
    switches from 1.0 to 1.5 scaling at sample 57, and both panes follow through `ScalingChanged`.
12. The Python reference is DPI-unaware, so at mixed DPI Windows bitmap-stretches each of its windows per monitor.
    That isn't a parity target; the DPI-baseline decision (per-monitor-v2, Tk px → DIPs 1:1) already covers it.

## Not yet verified

- Pane activation: clicking a pane activates it, so the main title bar goes inactive. Not compared against Tk yet;
  check it in the Scanner build slice.
