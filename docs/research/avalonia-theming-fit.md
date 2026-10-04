# Avalonia controls and theming vs. the sv_ttk dark UI

Research for issue #5 (map: issue #2). Question: **how well do Avalonia's current controls and
theming support a structurally faithful recreation of the sv_ttk dark UI?**

Researched 2026-10-03 against Avalonia 12.1.3 (latest stable), the vendored `src/sv_ttk/` theme, and
the widget usage in `src/` at commit `f95a07c`.

## Summary

The fit is good. Every tk/ttk widget the app uses has a built-in Avalonia 12 counterpart. All of them
ship in MIT-licensed packages, and most are in core `Avalonia.Controls`. Fluent dark can be pushed to
the sv_ttk palette through `ColorPaletteResources` plus per-control resource keys. Only Notebook tabs,
Labelframes and Buttons need real `ControlTheme` or template work to match sv_ttk's shapes. Two items
need care: the hierarchical Treeview replacement and the bundled font, which is a variable font.

### Recommendations

1. **Target Avalonia 12.1.x on `net10.0`.** 12.1.3 (2026-09-22) is the latest stable. Its packages
   ship `net10.0` and `net8.0` assets, and the v12 docs name .NET 10 as the recommended target.
2. **Use `<FluentTheme>` with `RequestedThemeVariant="Dark"` and a `Dark` `ColorPaletteResources`
   tuned to sv_ttk.** Set `Accent=#3CD648` and `RegionColor=#1C1C1C`, and use the grey ramp in the
   palette table below. On top of that, put a `ThemeDictionaries["Dark"]` block that overrides the
   specific Fluent resource keys (`TabItemHeader*`, `Button*`, `GroupBox*`, etc.), and add a few
   `ControlTheme`s for TabItem, GroupBox and Button. Don't write a theme from scratch.
3. **Treeview replacement: do not use TreeDataGrid** (it's paid, see below). Use the core controls:
   - **Scanner results** (2-level tree, optional right-aligned "mod" column, no headings) → **`TreeView`**
     with a `TreeDataTemplate` whose row is a two-column `Grid`.
   - **F4SE DLL table, `TreeWindow` modal, patcher file list** (all flat) → **`TableView`**. It is new in
     core Avalonia 12.1, read-only, has column headers and supports `CellTemplate`. Use `ListBox` where
     there are no headings.
   - Avoid **`DataGrid`**. It's still free (MIT) but deprecated, and gets bug fixes only.
4. **Bundle static Cascadia Mono TTFs, not the current file.** `src/assets/fonts/CascadiaMono.ttf` is a
   *variable* font (`fvar` table, `wght` axis 200–700). Avalonia doesn't support variable fonts yet,
   and the work is still open PRs. Ship `CascadiaMono-Regular.ttf` and `CascadiaMono-Bold.ttf` (OFL-1.1,
   from the official Cascadia Code release `static/` folder) as `AvaloniaResource`. Then the
   `bold underline` hyperlink labels render true bold, not synthetic bold.
5. **Convert font sizes from points to DIPs.** Tk sizes are points. Avalonia `FontSize` is in DIPs
   (1/96 in). 8/10/12/20 pt become **10.67/13.33/16/26.67** DIPs. Window sizes stay
   760×450 / 700×600, but see the DPI open question.
6. **Message boxes and file dialogs:** Avalonia has no `MessageBox`. Write a small themed dialog
   `Window` (preferred: it stays sv_ttk-styled and AOT-clean), or use MessageBox.Avalonia (MIT, has a
   12.0.0 build). Replace `filedialog.askopenfilename` with `IStorageProvider.OpenFilePickerAsync`.
7. **AOT:** Avalonia core, Fluent and CommunityToolkit.Mvvm 8.4.x are all built with
   `IsAotCompatible=true`. Compiled bindings are the default in v12. Avoid `ObservableValidator`
   (it is `RequiresUnreferencedCode`) and avoid `DataGrid` (not marked trimmable). NativeAOT still
   leaves native DLLs (Skia, HarfBuzz, ANGLE) beside the exe, so "one file" needs extra work either
   way. See the AOT section.

## Avalonia version and .NET 10 support

| Fact | Value | Source |
|---|---|---|
| Latest stable | **12.1.3**, published 2026-09-22 (11.3.22 is the maintained 11.x line) | <https://github.com/AvaloniaUI/Avalonia/releases> |
| 12.0.0 GA | 2026-04-07 | <https://github.com/AvaloniaUI/Avalonia/releases/tag/12.0.0> |
| Target frameworks in the `Avalonia` 12.1.3 nupkg | `net10.0`, `net8.0` | <https://api.nuget.org/v3-flatcontainer/avalonia/12.1.3/avalonia.nuspec> |
| .NET support policy | "Only .NET 8 and later are supported. The recommended target is .NET 10." | <https://docs.avaloniaui.net/docs/avalonia12-breaking-changes> |
| Licence | MIT (`Avalonia`, `Avalonia.Themes.Fluent`, `Avalonia.Desktop`, etc.) | nuspecs above |

v12 changes that matter for this port (all from the breaking-changes page):

- Compiled bindings are **on by default** (`AvaloniaUseCompiledBindingsByDefault=true`).
- The data-annotations validation plugin is **disabled by default**. It no longer conflicts with
  CommunityToolkit.Mvvm.
- `Window.SystemDecorations` is renamed `WindowDecorations` (`Full`/`BorderOnly`/`None`).
- The clipboard API changed: use the `SetTextAsync` / `TryGetTextAsync` extensions. This matters for
  the "Copy" buttons on the About tab.
- `DispatcherTimer` now binds to the *current* dispatcher. Create timers on the UI thread. This matters
  for the `after(3000, …)` "Copied!" revert and the progress polling.
- Custom fonts: v12 has its own font parser and supports TTF/OTF only.
- **`Avalonia.Diagnostics` (free DevTools) is removed.** DevTools now come with the paid "Plus" tier.
  The shipped app isn't affected, but developers lose the free F12 inspector.

## The sv_ttk dark palette

Taken from `src/sv_ttk/theme/dark.tcl` and `src/sv_ttk/sv.tcl`. This copy was run through the "Sun
Valley Theme Colorizer" (`LICENSE_MODIFICATIONS`), which swapped the stock blue accent for **green**.
Most of the drawn look (buttons, tabs, cards, checkboxes) comes from `spritesheet_dark.png`, not from
Tcl colour options. The sprite colours below were sampled from that PNG.

### Named theme colours (`dark.tcl` `colors` array, applied via `sv.tcl` `configure_colors`)

| sv_ttk key | Hex | Used for | Suggested Avalonia mapping |
|---|---|---|---|
| `-bg` | `#1c1c1c` | Window/frame background, field background, trough, Treeview background | `ColorPaletteResources.RegionColor`; window `Background` |
| `-fg` | `#fafafa` | Default foreground | `BaseHigh` (Fluent default is `#FFFFFF`) |
| `-disfg` | `#595959` | Disabled foreground (all widgets via `style map .`) | `*ForegroundDisabled` resource keys |
| `-selfg` | `#ffffff` | Selected text foreground | `TreeViewItemForegroundSelected` etc. |
| `-selbg` | `#05b62e` | Selection background, focus colour, `tk_setPalette` active background | `SystemAccentColorDark1`-ish; text selection brush |
| `-accent` | `#57ff64` | `TNotebook.Tab -focuscolor` | Focus adorner / `SystemAccentColorLight2` |

### Fixed colours in `dark.tcl` style maps

| Where | Hex |
|---|---|
| TButton fg disabled / pressed | `#7a7a7a` / `#d0d0d0` |
| Accent.TButton fg rest / pressed / disabled | `#000000` / `#252525` / `#a5a5a5` |
| TEntry fg disabled / pressed | `#757575` / `#cfcfcf` |
| Treeview selected row background | `#292929` (foreground `#ffffff`) |
| Menu background (non-Windows only) | `#292929` |
| Panedwindow sash | `#9e9e9e` |

### Sprite-derived colours (sampled centre / edge pixels of `spritesheet_dark.png`)

| Sprite | Fill | Edge / border | Notes |
|---|---|---|---|
| `button-rest` / `-hover` / `-pressed` / `-dis` | `#2a2a2a` / `#2f2f2f` / `#232323` / `#2f2f2f` | top `#313131`, bottom `#2c2c2c` | 4 px 9-slice border, i.e. rounded corners (~4 px) |
| `button-focus` | `#2a2a2a` | `#fafafa` | Focus ring is a white outline |
| `button-accent-rest` / `-hover` / `-pressed` / `-dis` | `#3cd648` / `#39c545` / `#35b340` / `#404040` | top `#4ad955`, bottom `#3bc347` | Accent fill **`#3cd648`** with black text |
| `card` (Labelframe border, Treeview field) | `#1c1c1c` | `#2f2f2f` | 5 px border, rounded |
| `notebook-border` | `#2f2f2f` | `#2f2f2f` | Tab-content frame |
| `tab-rest` / `tab-hover` / `tab-selected` | `#2f2f2f` / `#292929` / `#1c1c1c` | `#2f2f2f` | Selected tab merges with the page; unselected tabs are lighter |
| `heading-rest` / `-hover` / `-pressed` | `#2a2a2a` / `#2f2f2f` / `#232323` | `#1c1c1c` separators | Treeview column headings |
| `check-unsel-rest` / `-hover` | `#1c1c1c` / `#262626` | `#989898` | Unchecked box outline |
| `check-rest` (checked) | `#3cd648` | `#3cd648` | Accent-filled box, dark tick |
| `radio-unsel-rest` / `radio-rest` | `#191919` / accent ring `#3cd648`, black centre | `#929292` | |
| `textbox-rest` / `-hover` / `-focus` / `-dis` | `#292929` / `#2f2f2f` / `#1c1c1c` / `#262626` | bottom line `#989898`; focus bottom `#3cd648` | Same bottom-accent idiom as Fluent `TextBox` |
| `progressbar-trough-hor` / `-bar-hor` | `#989898` thin line on `#1c1c1c` / `#48d651` | | Thin bar, close to Fluent `ProgressBar` |
| `scrollbar-trough-vert` / `-thumb-vert` | `#292929` / `#9e9e9e` | | Always visible, with arrow buttons |
| `sep` | `#2f2f2f` | | `ttk.Separator` |
| `switch-rest` (unused by app) | `#3cd648` | | |

### App-level colours (`src/globals.py`, layered on top of sv_ttk)

| Constant | Value | Hex | Used for |
|---|---|---|---|
| `COLOR_DEFAULT` | `#CACACA` | `#cacaca` | Foreground forced onto Tab, TButton, TCheckbutton, TRadiobutton, Labelframe label, Treeview, Heading (`utils.set_theme`) |
| `COLOR_GOOD` | `#619267` | `#619267` | Good status, log success, F4SE tag |
| `COLOR_BAD` | `#AF5A66` | `#af5a66` | Bad status, log errors |
| `COLOR_INFO` | `dodger blue` | `#1e90ff` | Log info |
| `COLOR_NEUTRAL_1` | `gray` | `#808080` | F4SE neutral rows |
| `COLOR_NEUTRAL_2` | `bisque` | `#ffe4c4` | F4SE about text, scanner result count |
| `COLOR_WARNING` | `orange` | `#ffa500` | Warnings |
| `COLOR_NOTE` | `#C5C464` | `#c5c464` | Notes |
| `COLOR_INDIE` | `#4b92da` | `#4b92da` | "Independent" DLL tag |
| `Update.TFrame` background | `pale green` | `#98fb98` | Update-available banner |

Tk named colours come from the Tk 8.6 colour table (<https://www.tcl-lang.org/man/tcl8.6/TkCmd/colors.htm>).
Tk 8.6 `gray` is `#808080`, the same as Avalonia `Colors.Gray`.

### Fonts in play

- **App font:** `Cascadia Mono` at 8/10/12/20 pt (`FONT_SMALLER`/`FONT_SMALL`/`FONT`/`FONT_LARGE`),
  loaded privately with `AddFontResourceExW(..., FR_PRIVATE)` (`utils.load_font`). It's applied to
  almost every widget via `font=` or `style.configure`.
- **sv_ttk defaults**, which show up wherever the app doesn't override: `Segoe UI Variable Text` 14 px
  (body: Entry, unstyled Labels) and `Segoe UI Variable Small` 12 px (caption: Heading, Labelframe label
  before the app overrides it). Negative Tk sizes are pixels.

### Fluent palette fit

`ColorPaletteResources` exposes `Accent`, `AltHigh…AltMediumLow`, `BaseHigh…BaseMediumLow`,
`ChromeAltLow…ChromeWhite`, `ErrorText`, `ListLow`, `ListMedium` and `RegionColor`
(<https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/src/Avalonia.Themes.Fluent/ColorPaletteResources.Properties.cs>).
You override only the properties you set, per `Light`/`Dark` key. Only `Accent` can change at runtime;
the rest are read once at startup. FluentTheme supports only Dark and Light variants
(<https://docs.avaloniaui.net/docs/styling/themes>). Fluent dark defaults are pure black/white
(`RegionColor #000000`, `ChromeMediumLow #2B2B2B`, `ChromeLow #171717`;
<https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/src/Avalonia.Themes.Fluent/Accents/BaseColorsPalette.xaml>),
so you need the overrides to get sv_ttk's `#1c1c1c` / `#2a2a2a` / `#2f2f2f` greys.

The palette alone won't reach sv_ttk because Fluent derives control brushes from *alpha* overlays
(`BaseLow = #33FFFFFF`, etc.), while sv_ttk uses opaque greys. The reliable approach is three layers:

1. **Palette:** `Accent=#3CD648`, `RegionColor=#1C1C1C`, `BaseHigh=#FAFAFA`, `ErrorText=#AF5A66`.
2. **Control resource keys in `ResourceDictionary.ThemeDictionaries` → `Dark`.** These must be consumed
   via `DynamicResource`, and Fluent's control themes already do that
   (<https://docs.avaloniaui.net/docs/styling/theme-variants>). Example: TabItem exposes
   `TabItemHeaderBackgroundSelected/Unselected/…PointerOver`, `TabItemHeaderForeground*`,
   `TabItemHeaderFontSize` and `TabItemHeaderSelectedPipeFill`
   (<https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/src/Avalonia.Themes.Fluent/Controls/TabItem.xaml>).
   Button, CheckBox, RadioButton, TextBox, GroupBox, ToolTip, ScrollBar, TreeViewItem and TableView
   follow the same pattern.
3. **`ControlTheme`s (`BasedOn` the Fluent theme)** where the *shape* differs. That's TabItem: sv_ttk
   draws boxed tabs with a border, while Fluent draws a pivot-style underline "pipe". It's also
   GroupBox, where the Scan Settings pane centres its label (`labelanchor=N`) but Fluent's GroupBox puts
   the header in a fixed left column.

## Widget inventory and mapping

Inventory from grepping `src/` (excluding `src/sv_ttk/`). Counts are constructor calls.

| tk/ttk widget / feature | Use sites (`src/…`) | Avalonia replacement | Package / licence | Fidelity notes |
|---|---|---|---|---|
| `Tk` root, fixed size, not resizable, centred, dark title bar | `main.py`, `cm_checker.py` (`wm_resizable(False, False)`, 760×450, `wm_iconphoto`), `utils.set_titlebar_style` (DWM attrs 19/20) | `Window` with `Width=760 Height=450 CanResize=False WindowStartupLocation=CenterScreen Icon=…` | Avalonia (MIT) | `CanResize=False` also forces `CanMaximize=False` (<https://docs.avaloniaui.net/docs/how-to/window-how-to>). The dark title bar comes from `RequestedThemeVariant="Dark"`: "Changing a window's RequestedThemeVariant also affects window decoration variants on platforms where this is supported" (<https://docs.avaloniaui.net/docs/styling/theme-variants>), so the DWM P/Invoke is likely unnecessary (verify). |
| `ttk.Notebook` (6 tabs) | `cm_checker.py`, `helpers.CMCTabFrame`, every `tabs/*.py` | `TabControl` + `TabItem` | Avalonia (MIT) | Needs a TabItem `ControlTheme` for sv_ttk's boxed tabs (`tab-rest #2f2f2f`, `tab-selected #1c1c1c` merged into a `#2f2f2f`-bordered page, 32 px high, padding 16/14/16/6). Font: Cascadia Mono 12 pt → 16 DIP via `TabItemHeaderFontSize`. The app removes the focus dotted line; Fluent's focus adorner can be disabled the same way. `<<NotebookTabChanged>>` becomes `SelectedIndex`/`SelectedItem` binding. |
| `ttk.Label` (~70; text, image-only `compound="image"`, image+text `compound=LEFT`, `wraplength`, `justify`) | Mostly `tabs/_overview.py` (34) and `tabs/_scanner.py` (13), plus `_about.py`, `downgrader.py`, `modal_window.py`, `cm_checker.py`, `helpers.py` | `TextBlock` (text), `Image` (icon only), `StackPanel{Image, TextBlock}` (compound) | Avalonia (MIT) | `wraplength` → `TextWrapping=Wrap` + `MaxWidth`. `justify` → `TextAlignment`. `textvariable` → binding. Per-label `foreground` → `Foreground`. Hover swap (`_overview.py` `<Enter>`/`<Leave>` version labels) → `:pointerover` style or code-behind. |
| Hyperlink labels (`cursor="hand2"`, `<Button-1>` → `webbrowser.open`/`os.startfile`, bold underline, `<Button-3>` copy) | `cm_checker.py` (Nexus/GitHub), `tabs/_overview.py` (game path, info icons), `tabs/_scanner.py` (file path, solution URL) | `HyperlinkButton` (Fluent theme exists) or `TextBlock` with `Cursor="Hand"` + `PointerPressed` / `ContextRequested` | Avalonia (MIT) | Restyle `HyperlinkButton` foreground and underline to match. Bold depends on the font fix (recommendation 4). |
| `ttk.Button` (~25; `Accent.TButton` style, `padding`, `state=DISABLED`, image-only buttons) | `_about.py` (6), `_scanner.py` (9), `_overview.py` (3), `downgrader.py`, `modal_window.py`, `patcher/_base.py`, `_tools.py`, `utils.copy_text_button` | `Button`; accent → `Classes="accent"` | Avalonia (MIT) | Fluent already has an accent button class, re-coloured to `#3cd648` with black text. sv_ttk buttons are flat `#2a2a2a` with a lighter top edge. Override `ButtonBackground*`/`ButtonBorderBrush*` resources. `command=` → `Command` (RelayCommand). Text changes like "Scanning…"/"Copied!" become bound properties. |
| `ttk.Labelframe` (~13; `labelanchor=N` on Scan Settings, `padding`) | `tabs/_overview.py` (4), `downgrader.py` (4), `_tools.py` (2), `_settings.py`, `_scanner.py` (SidePane), `patcher/_archives.py` | **`GroupBox`** (new in Avalonia 12, PR #19366, July 2025) | Avalonia (MIT) | Fluent GroupBox draws a notched outline with the header in the gap, left-aligned, using `ControlCornerRadius`. sv_ttk draws a `card` sprite (`#1c1c1c` fill, `#2f2f2f` rounded border) with the label on top. Restyle with `GroupBox*` resources, or a `ControlTheme` if the notch looks wrong. Centred header (Scan Settings) needs a template tweak (<https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/src/Avalonia.Themes.Fluent/Controls/GroupBox.xaml>). |
| `ttk.Checkbutton` (variable-bound, tooltips) | `tabs/_scanner.py` (one per `ScanSetting`), `downgrader.py` (2) | `CheckBox` | Avalonia (MIT) | Fluent's checked state is accent-filled, so it already resembles sv_ttk (`#3cd648` box). Unchecked outline is `#989898` in sv_ttk; tune `CheckBoxCheckBackgroundStroke*`. |
| `ttk.Radiobutton` (variable-bound) | `tabs/_settings.py` (update source, log level), `downgrader.py` (2), `patcher/_archives.py` (2) | `RadioButton` with `GroupName` | Avalonia (MIT) | Close match: sv_ttk uses an accent ring with a dark centre, like Fluent. |
| `ttk.Entry` (filter box, `<KeyRelease>`) | `patcher/_archives.py` | `TextBox` | Avalonia (MIT) | Fluent has the same bottom-accent-on-focus idiom as `textbox-focus`. Use `Text` binding with `UpdateSourceTrigger=PropertyChanged` or a `TextChanged` handler. |
| `ttk.Treeview` (6 instances) | See the Treeview section | `TreeView` / `TableView` / `ListBox` | Avalonia (MIT) | See the Treeview section. |
| `ttk.Scrollbar` (paired with Treeview/Text) | `tabs/_f4se.py`, `tabs/_scanner.py`, `modal_window.py`, `patcher/_base.py`, `logger.py` | Implicit `ScrollViewer` inside each control | Avalonia (MIT) | Fluent scrollbars auto-hide to a thin overlay. sv_ttk's are always visible with arrows. Set `ScrollViewer.AllowAutoHide="False"` and restyle `ScrollBar` (thumb `#9e9e9e`, trough `#292929`). |
| `ttk.Progressbar` (determinate, `variable`) | `tabs/_scanner.py`, `downgrader.py` | `ProgressBar` | Avalonia (MIT) | Thin accent bar, close match. Bind `Value`. |
| `ttk.Separator` | `utils.add_separator` | `Separator` or a 1 px `Border` `#2f2f2f` | Avalonia (MIT) | Trivial. |
| `ttk.Frame` (layout; `Update.TFrame` pale-green banner) | everywhere; `cm_checker.py` banner | `Grid` / `StackPanel` / `DockPanel` / `Border Background="#98fb98"` | Avalonia (MIT) | tk `grid`/`pack` map directly onto `Grid`/`DockPanel`/`StackPanel`. |
| `tk.Text`, read-only, coloured tag ranges | `tabs/_f4se.py` (static help text with 5 colour tags), `logger.py` (append-only log, coloured emoji prefix, auto-scroll), `helpers.py` (stderr error window) | `SelectableTextBlock` / `TextBlock` with `Inlines` (`Run Foreground=…`). For the log, an `ItemsControl`/`ListBox` of entries in a `ScrollViewer` with scroll-to-end | Avalonia (MIT) | No need for a rich-text editor (the Pro-tier "Rich Text Editor" isn't needed). Test colour emoji (❌ ✅ 💭) rendering. |
| Tooltips (`tktooltip.ToolTip`, ~35 sites) | `cm_checker.py`, `downgrader.py`, `tabs/_overview.py` (most), `_scanner.py`, `_settings.py`, `_tools.py` | `ToolTip.Tip` attached property | Avalonia (MIT) | tkinter-tooltip defaults: `delay=0`, `follow=True`, offset +10/+10, a `tk.Message` in a 1 px black-bordered Toplevel using the palette colours (<https://github.com/gnikit/tkinter-tooltip/blob/main/tktooltip/tooltip.py>). Avalonia: `ToolTip.ShowDelay` (default 400 ms) → 0, `Placement=Pointer`, `HorizontalOffset`/`VerticalOffset` (<https://docs.avaloniaui.net/controls/feedback/tooltip>). Avalonia doesn't follow the pointer continuously; that's an accepted minor difference. Dynamically created tooltips (`_scanner.py` details pane) become bound `ToolTip.Tip`. |
| Modal `Toplevel` with `transient` + `grab_set`, fixed size, centred on parent, `<Escape>`/`<space>` to close | `modal_window.ModalWindow` → `AboutWindow`, `TreeWindow`, `Downgrader`, `PatcherBase`/`ArchivePatcher`; nested (Downgrader opens AboutWindow) | `Window.ShowDialog(owner)` with `CanResize=False`, `WindowStartupLocation=CenterOwner`, `ShowInTaskbar=False`, `KeyBindings` for Esc/Space | Avalonia (MIT) | "The dialog appears centered over the owner window and prevents interaction with it until closed" (<https://docs.avaloniaui.net/docs/how-to/dialogs-how-to>). This replaces the manual grab save/restore in `ModalWindow._ungrab_and_destroy`. Nested dialogs work by passing the dialog as owner. |
| Borderless docked panes (`wm_overrideredirect(True)` Toplevels glued to the main window's right and bottom edges, repositioned on `<Configure>`, hidden on minimise) | `tabs/_scanner.py` `SidePane` (200 px wide, right of the window), `ResultDetailsPane` (200 px high, below the window) | `Window` with `WindowDecorations="None"`, `ShowInTaskbar=False`, `CanResize=False`, shown via `Show(owner)`, repositioned on the owner's `PositionChanged`/`Resized` | Avalonia (MIT) | `Show(owner)` keeps the pane above its owner (<https://docs.avaloniaui.net/docs/how-to/window-how-to>), and Windows minimises owned windows with the owner. That removes the `<Map>`/`<Unmap>` and `<FocusIn>` `tkraise` hacks. `Position` is a `PixelPoint` (physical pixels) while sizes are DIPs, so this needs a DPI test. **Highest structural risk.** |
| `messagebox.askyesno` / `showerror` / `showwarning` | `game_info.py`, `tabs/_overview.py` | Custom themed dialog `Window`, or MessageBox.Avalonia | Avalonia (MIT); MessageBox.Avalonia (MIT, 12.0.0 on NuGet) | "Avalonia does not include a built-in MessageBox control" (<https://docs.avaloniaui.net/troubleshooting/controls/messagebox>). tk's messagebox is the native Win32 one; a Win32 `MessageBoxW` P/Invoke is a third option that matches today's look exactly. |
| `filedialog.askopenfilename` | `game_info.py` (locate Fallout4.exe) | `TopLevel.StorageProvider.OpenFilePickerAsync` | Avalonia (MIT) | Native Windows picker, same as tk. |
| Images (`PhotoImage` PNGs, cached in `CMChecker.get_image`) | `src/assets/images/*.png` (icons 16/20/24/32/256 px, logos), `icon.ico` | `Image Source="avares://…"` / `Bitmap`; window `Icon` | Avalonia (MIT) | Mark assets as `AvaloniaResource`. Set `RenderOptions.BitmapInterpolationMode` for small icons if they blur. |
| Fonts (`AddFontResourceExW` private load; `font=(…, "bold underline")`) | `utils.load_font`, `globals.py` | `FontFamily="avares://App/Assets/Fonts#Cascadia Mono"` or an `EmbeddedFontCollection` registered with `ConfigureFonts` | Avalonia (MIT); font OFL-1.1 | Documented in <https://docs.avaloniaui.net/docs/styling/custom-fonts>. **Variable fonts not supported** (same page, issue #11092), so ship static weights. `TextDecorations="Underline"` for underline. |
| `StringVar`/`BooleanVar`/`IntVar`/`DoubleVar` | throughout | `[ObservableProperty]` on CommunityToolkit.Mvvm view models | CommunityToolkit.Mvvm (MIT) | Direct conceptual mapping. |
| `root.after(ms, …)` polling / revert timers | `tabs/_scanner.py`, `downgrader.py`, `utils.copy_text_button` | `DispatcherTimer`, or `async` + `Task.Delay`, or `IProgress<T>` from the worker | Avalonia (MIT) | Prefer `async`/`IProgress<T>` over polling. Create `DispatcherTimer` on the UI thread (v12 change). |
| `ttk.Style().configure(...)` global fonts and colours | `utils.set_theme`, `tabs/_scanner.py` (Treeview font) | App-level `Styles` (`Style Selector="Button"` etc.) + resources | Avalonia (MIT) | One `Styles/SvDark.axaml` file. |

No `Canvas`, `Menu`, `Listbox`, `Spinbox`, `Combobox`, `Scale` or `PanedWindow` is used, so those sv_ttk
elements have no counterpart to port.

## Treeview replacement

### What the app does with `ttk.Treeview`

| Site | Shape | Headings | Columns | Selection | Per-row colour |
|---|---|---|---|---|---|
| `tabs/_scanner.py` `tree_results` | **2-level tree**: group rows (problem type, `open=True`) → problem rows; collapse/expand-all buttons | No (`show="tree"`) | `#0` + optional right-aligned `mod` column (stage mode) | none while scanning, then single (`browse`) → `<<TreeviewSelect>>` opens the details pane | no |
| `tabs/_f4se.py` `tree_dlls` | Flat | Yes: DLL / OG / NG / AE / Your Game | 5, fixed widths 240/60/60/60/80, centred | default | **yes** (5 tags → foreground colours) |
| `modal_window.TreeWindow` | Flat | Optional | `#0` (65 px) + 1–n stretch columns | none | no |
| `patcher/_base.py` `_tree_files` | Flat, single column | No | 1 | default | no |

Only one site is hierarchical, and it shows no headings. None of the sites need editing, sorting,
column resize or virtualisation of huge sets.

### Candidates

| Control | Hierarchy | Columns + headers | Package | Licence / distribution status (2026-10) | Verdict |
|---|---|---|---|---|---|
| **`TreeView`** | Yes | No built-in columns. A `TreeDataTemplate` with a `Grid` per row can fake an aligned column. | Core `Avalonia.Controls` | MIT | **Use for scanner results.** Matches `show="tree"` exactly. Fluent `TreeView`/`TreeViewItem` themes exist. |
| **`TableView`** | No (flat) | Yes: `TableViewColumn` with `Header`, `Binding`, `CellTemplate`, `Width` (px or `*`), `HorizontalContentAlignment`, `CanUserResize` | Core `Avalonia.Controls`, **new in 12.1** (PR #21511) | MIT | **Use for the F4SE table and `TreeWindow`.** `CellTemplate` covers coloured cells. It derives from `ListBox`, so `SelectionMode` applies. Rows are virtualised (<https://docs.avaloniaui.net/controls/data-display/structured-data/tableview>, <https://github.com/AvaloniaUI/Avalonia/pull/21511>). |
| `ListBox` | No | No | Core | MIT | Patcher file list (single column, no header). |
| `DataGrid` | No (row grouping only) | Yes | `Avalonia.Controls.DataGrid` 12.1.2, now in its own repo | **MIT, still free**, but the README says: "`DataGrid` is deprecated and only receives bug fixes. To display read-only tabular data, we recommend using TableView." It's not marked trimmable/AOT-compatible in its csproj. | Avoid (<https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid>). |
| `TreeDataGrid` | Yes | Yes | `Avalonia.Controls.TreeDataGrid` 12.3.1 | **Commercial.** The OSS repo is archived. Its README says: "The `TreeDataGrid` control is moving to Avalonia Accelerate as a commercially supported component" (<https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid>). Accelerate has since been folded into Avalonia's tiers, and "Tree DataGrid" is listed under **Pro, €899/seat/year** (<https://avaloniaui.net/pricing>). The 12.x nupkg has no licence expression, author `AvaloniaUI OÜ`, and depends on `AvaloniaUI.Licensing`. 11.2.1 release notes already mention "licensing messaging when no key is available" (<https://api.nuget.org/v3-flatcontainer/avalonia.controls.treedatagrid/12.3.1/avalonia.controls.treedatagrid.nuspec>). | **Ruled out.** It's paid, the NuGet build is proprietary and therefore not GPL-compatible to ship, and it isn't needed. The last MIT source (archived repo, 11.x) could be forked, but it targets Avalonia 11 and would be ours to maintain. Not worth it for one two-level list. |

**Verdict:** `TreeView` (scanner) + `TableView` (F4SE, TreeWindow) + `ListBox` (patcher). All of them are
core, MIT, and themed by Fluent. TableView needs Avalonia ≥ 12.1.

## Tooltips, dialogs, fixed windows, fonts, images

Covered row by row in the mapping table. Key points:

- **Tooltips:** `ToolTip.Tip` plus `ToolTip.ShowDelay="0"` as an app-wide style setter. Theme via
  `ToolTip*` resources to get `#1c1c1c` background, `#fafafa` text and a 1 px black border.
- **Modal dialogs:** `await dialog.ShowDialog(owner)` is modal to its owner and centres with
  `CenterOwner`. Nested modals (Downgrader → About) are fine.
- **Fixed-size windows:** `Width`/`Height` + `CanResize=False`. On Windows this hides the maximise
  button. Avalonia `Width`/`Height` are the client area, same as tk `wm_geometry`.
- **Bundled font:** `AvaloniaResource Include="Assets\Fonts\*"` and
  `FontFamily="avares://<Asm>/Assets/Fonts#Cascadia Mono"`. The family name must be the internal name;
  for the bundled file that's `Cascadia Mono` (name ID 1), version 2404.023. Swap the variable TTF for
  static Regular/Bold.
- **Images/icons:** PNGs via `avares://`. `icon.ico` via `Window.Icon`. Image-only labels become `Image`
  controls; image buttons become `Button` with `Image` content.

## NativeAOT / trimming status

| Component | AOT/trim status | Source |
|---|---|---|
| Avalonia core, Fluent, Desktop, Win32, Skia (12.1.3) | Built with `IsTrimmable=true` and `IsAotCompatible=true` for `net8.0+`, trim analyser on, `DisableRuntimeMarshalling` | <https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/build/TrimmingEnable.props> |
| Avalonia NativeAOT guidance | `PublishAot=true`. `BuiltInComInteropSupport=false` was only "necessary before Avalonia 12.0". Use compiled bindings, `AvaloniaResource` assets, no runtime XAML loading. Reflection-heavy third-party controls may break. | <https://docs.avaloniaui.net/docs/deployment/native-aot> |
| Compiled bindings | Default in v12 (`AvaloniaUseCompiledBindingsByDefault=true`); `{Binding}` compiles to `CompiledBinding`. Requires `x:DataType` on views. | <https://docs.avaloniaui.net/docs/avalonia12-breaking-changes> |
| CommunityToolkit.Mvvm 8.4.2 | `IsAotCompatible=true` on `net8.0+` (`src/Directory.Build.targets`). Source generators (`[ObservableProperty]`, `[RelayCommand]`) are reflection-free. **`ObservableValidator` is `[RequiresUnreferencedCode]`**, so don't use it. MVVMTK0045/0050 AOT warnings apply only to WinRT (WinUI/UWP), not Avalonia. Partial-property `[ObservableProperty]` needs `LangVersion` preview/C# 14, which the .NET 10 SDK has. | <https://github.com/CommunityToolkit/dotnet/blob/rel/8.4.2/src/Directory.Build.targets>, <https://github.com/CommunityToolkit/dotnet/blob/rel/8.4.2/src/CommunityToolkit.Mvvm/ComponentModel/ObservableValidator.cs>, <https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/generators/errors/mvvmtk0045> |
| `Avalonia.Controls.DataGrid` | No `IsTrimmable`/`IsAotCompatible` in its csproj or `Directory.Build.props` | <https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid> |
| `IsAotCompatible` metadata check | The assembly metadata only exists in libraries built for .NET 10+. Opt-in IL3058 verification warns on unannotated references. | <https://learn.microsoft.com/dotnet/core/deploying/native-aot/#aot-compatibility-analyzers> |

**Facts the packaging decision depends on:**

- **Native DLLs ship either way.** Avalonia on Windows pulls `SkiaSharp` 3.119.4 (`libSkiaSharp.dll`),
  `HarfBuzzSharp` 8.3.1.3 (`libHarfBuzzSharp.dll`) and `Avalonia.Angle.Windows.Natives` (ANGLE GLES)
  (<https://api.nuget.org/v3-flatcontainer/avalonia.skia/12.1.3/avalonia.skia.nuspec>,
  <https://api.nuget.org/v3-flatcontainer/avalonia.win32/12.1.3/avalonia.win32.nuspec>).
  - **Self-contained single-file** bundles only managed DLLs. With
    `IncludeNativeLibrariesForSelfExtract=true` the natives are embedded but **extracted to
    `%TEMP%\.net`** at startup
    (<https://learn.microsoft.com/dotnet/core/deploying/single-file/overview#native-libraries>).
  - **NativeAOT** produces one native exe plus those native DLLs beside it, unless they are statically
    linked (not provided by the NuGet packages).
- **.NET 10 change:** single-file apps only probe the app directory for native libraries when the
  search path includes `AssemblyDirectory`. That's the default for P/Invoke, but it's worth knowing
  (<https://learn.microsoft.com/dotnet/core/compatibility/interop/10.0/native-library-search>).
- **NativeAOT on Windows needs the VS "Desktop development with C++" workload at build time**, which
  matters for CI. Other limits: no built-in COM, no `Reflection.Emit`, trimming required
  (<https://learn.microsoft.com/dotnet/core/deploying/native-aot/>). The app's non-UI code (pywin32
  equivalents, xdelta, version-info reading) must avoid built-in COM.

## Licensing summary

| Component | Licence | GPL-2.0-or-later compatible | Free |
|---|---|---|---|
| Avalonia (+Fluent, Desktop, Skia, HarfBuzz) | MIT | Yes | Yes |
| Avalonia.Controls.DataGrid | MIT | Yes | Yes (deprecated) |
| Avalonia.Controls.TreeDataGrid 12.x | Proprietary (Avalonia Pro) | **No** | **No** |
| CommunityToolkit.Mvvm | MIT | Yes | Yes |
| MessageBox.Avalonia (optional) | MIT | Yes | Yes |
| Cascadia Mono font | SIL OFL 1.1 | Yes (font bundling is permitted) | Yes |
| sv_ttk (reference only, not ported as code) | MIT | Yes | Yes |
| Avalonia DevTools (dev-time only) | Paid (Plus tier) | n/a, not shipped | No. Avoid depending on it. |

## Risks and open questions for the prototype ticket

1. **Docked borderless side/details panes** (`_scanner.py`). Check that `WindowDecorations=None` +
   `Show(owner)` windows track the main window on move/resize/minimise/restore with no lag or z-order
   flicker, at 100%, 150% and mixed-DPI monitors. `Position` is in physical pixels and sizes are in
   DIPs. Fallback if it's janky: draw the panes *inside* a wider main window, but that changes the
   window size, a structural deviation that needs sign-off.
2. **DPI baseline.** Find out whether the current PyInstaller build is DPI-aware. Nothing in `src/` calls
   `SetProcessDpiAwareness`. If it isn't, Windows bitmap-scales the 760×450 tk window, and Avalonia's
   760×450 DIPs will match its *size* while rendering crisper. If it is, tk pixels = physical pixels
   and the Avalonia window will look larger at >100%. Screenshot both at 150%.
3. **Font metrics.** With static Cascadia Mono, check that 12 pt → `FontSize=16` reproduces label
   widths and wrapping (`wraplength` values are in pixels) in the Overview grid and the scanner details
   pane. Check that synthetic vs. real bold is fixed.
4. **TabItem and GroupBox `ControlTheme`s.** Prototype boxed tabs and the card-style Labelframe;
   compare screenshots with the Python app side by side.
5. **TableView fit for F4SE:** fixed column widths, centred cells, per-row foreground via
   `CellTemplate`, header styling against sv_ttk `heading-*` sprites. TableView is new (12.1), so look
   for rough edges.
6. **Always-visible scrollbars** with `AllowAutoHide=False`, plus a custom ScrollBar theme with arrow
   buttons.
7. **Colour emoji** in the log (`logger.py`: ❌ ✅ 💭) and in any labels. Confirm Avalonia/Skia falls
   back to Segoe UI Emoji in colour.
8. **Dark title bar** via `RequestedThemeVariant="Dark"` alone. Confirm on Windows 10 and 11 before
   dropping the DWM P/Invoke.
9. **AOT smoke test:** publish the prototype with `PublishAot=true` and `IsAotCompatible=true` +
   IL3058 verification. Record exe size, the native DLL list and startup time against self-contained
   single-file with `IncludeNativeLibrariesForSelfExtract`. This feeds the packaging decision.
10. **Message box approach:** custom themed dialog vs. Win32 `MessageBoxW` vs. MessageBox.Avalonia.
    Decide in the prototype; a custom dialog is the most faithful to the dark theme.

## Sources

- Avalonia releases: <https://github.com/AvaloniaUI/Avalonia/releases>
- Avalonia 12 breaking changes: <https://docs.avaloniaui.net/docs/avalonia12-breaking-changes>
- Fluent theme / custom palettes: <https://docs.avaloniaui.net/docs/styling/themes>
- Theme variants / ThemeDictionaries: <https://docs.avaloniaui.net/docs/styling/theme-variants>
- Custom fonts: <https://docs.avaloniaui.net/docs/styling/custom-fonts>
- ToolTip: <https://docs.avaloniaui.net/controls/feedback/tooltip>
- Windows / dialogs: <https://docs.avaloniaui.net/docs/how-to/window-how-to>, <https://docs.avaloniaui.net/docs/how-to/dialogs-how-to>
- MessageBox: <https://docs.avaloniaui.net/troubleshooting/controls/messagebox>
- TableView: <https://docs.avaloniaui.net/controls/data-display/structured-data/tableview>
- Native AOT (Avalonia): <https://docs.avaloniaui.net/docs/deployment/native-aot>
- Fluent theme source (12.1.3): <https://github.com/AvaloniaUI/Avalonia/tree/release/12.1.3/src/Avalonia.Themes.Fluent>
- Trimming props: <https://github.com/AvaloniaUI/Avalonia/blob/release/12.1.3/build/TrimmingEnable.props>
- DataGrid repo: <https://github.com/AvaloniaUI/Avalonia.Controls.DataGrid>
- TreeDataGrid repo (archived): <https://github.com/AvaloniaUI/Avalonia.Controls.TreeDataGrid>
- Avalonia pricing: <https://avaloniaui.net/pricing>
- NuGet flat-container metadata: <https://api.nuget.org/v3-flatcontainer/{avalonia,avalonia.controls.datagrid,avalonia.controls.treedatagrid,communitytoolkit.mvvm}/index.json>
- CommunityToolkit.Mvvm: <https://github.com/CommunityToolkit/dotnet/tree/rel/8.4.2>
- .NET single-file: <https://learn.microsoft.com/dotnet/core/deploying/single-file/overview>
- .NET Native AOT: <https://learn.microsoft.com/dotnet/core/deploying/native-aot/>
- Variable-font tracking: <https://github.com/AvaloniaUI/Avalonia/issues/11092>, <https://github.com/AvaloniaUI/Avalonia/pull/22179>
- tkinter-tooltip: <https://github.com/gnikit/tkinter-tooltip/blob/main/tktooltip/tooltip.py>
- Tk colour names: <https://www.tcl-lang.org/man/tcl8.6/TkCmd/colors.htm>
