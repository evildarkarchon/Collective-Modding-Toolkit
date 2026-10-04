# Port to C#/Avalonia as a sibling tree, at behaviour parity

The app is being rewritten from Python/tkinter to C# (.NET 10) with Avalonia, in a sibling tree in this repo rather than a new repo or an in-place replacement, so the Python app stays runnable and diffable as the Reference Implementation until cutover. The port targets Behaviour Parity with a frozen Parity Baseline (`f95a07c`): bugs found during the port are logged as separate issues instead of fixed inline, and the C# app ships only at full parity, as a drop-in replacement (`cm-toolkit.exe`, same `settings.json` keys, same `cm-toolkit.log`).

## Considered Options

- **Pixel-faithful UI** (recreating sv_ttk's sprite-based rendering) — rejected as costly for little user-visible gain; the UI is structurally faithful on Fluent dark tuned to the sv_ttk palette instead.
- **Fix bugs during the port** — rejected because it makes parity uncheckable.
- **Cross-platform (Linux/Proton) support** — out of scope for this effort; managed .NET APIs are preferred over P/Invoke to keep it possible later.
