# Port to C#/Avalonia as a sibling tree, at behaviour parity

The app is being rewritten from Python/tkinter to C# (.NET 10) with Avalonia, in a sibling tree in this repo rather than a new repo or an in-place replacement, so the Python app stays runnable and diffable as the Reference Implementation until cutover. The port targets Behaviour Parity with a frozen Parity Baseline (`f95a07c`): bugs found during the port are logged as separate issues instead of fixed inline, and the C# app ships only at full parity, as a drop-in replacement (`cm-toolkit.exe`, same `settings.json` keys, same `cm-toolkit.log`).

> **Superseded in part (2026-10-12):** the clause that bugs are "logged as separate issues instead of fixed" is superseded by [ADR-0006](0006-port-fixes-reference-bugs-as-deviations.md). The port now fixes each open `bug` issue as a documented Deviation, in a fix phase after the slices merge. The rest of this decision stands.

> **Amended (2026-10-29):** the port is done and [ADR-0007](0007-parity-process-retired.md) retired the parity process. Behaviour Parity no longer constrains changes; the drop-in compatibility stays as a product decision.

## Considered Options

- **Pixel-faithful UI** (recreating sv_ttk's sprite-based rendering) — rejected as costly for little user-visible gain; the UI is structurally faithful on Fluent dark tuned to the sv_ttk palette instead.
- **Fix bugs during the port** — rejected because it makes parity uncheckable. (Reopened in ADR-0006: fixes now land after the slices, each as a Deviation the harness checks.)
- **Cross-platform (Linux/Proton) support** — out of scope for this effort; managed .NET APIs are preferred over P/Invoke to keep it possible later.
