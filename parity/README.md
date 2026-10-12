# Parity harness

How the C# port proves **Behaviour Parity** with the Reference Implementation in `src/`
([ADR-0004](../docs/adr/0004-parity-proved-by-recorded-scenarios.md)). Each **Parity Scenario** is a fake machine. A
Python driver records once what the Reference Implementation does on it (the *golden*), and C# tests replay the scenario
against Core or the App and compare. CI never runs Python: the goldens are committed. The checklist being proved is the
[Parity Inventory](../docs/parity-inventory.md).

The harness is built once. Each feature slice authors and records its own scenarios, adds the hand-written tests
and screenshot pairs it needs, and adds the IDs it proves to `covered.txt`.

## Layout

| Path | What it is |
|---|---|
| `scenarios/<module>/<scenario>/` | One scenario: `tree/`, `scenario.json`, optional `http/`, the recorded `golden.json`, and a `golden.port.json` if the port deviates ([Deviations](#deviations-fixing-a-reference-bug)). Kept after cutover as plain regression fixtures. |
| `driver/` | The Python recorder and screenshot script. **Deleted at cutover**, when the Reference Implementation goes. |
| `assets/` | Sources and outputs for synthetic binaries (version stubs, F4SE DLLs, CRC32-forged files, deltas). Each toolchain lands with the first slice that needs it. |
| `screenshots/<slice>/` | Screenshot pairs against the Tk reference, approved by a human in the slice PR. |
| `covered.txt` | The ratchet: IDs proven so far. |
| `coverage-manual.json` | Proofs that are neither a scenario nor a test: exemptions, manual checks, screenshot approvals. |

The C# side lives in `dotnet/tests/CMToolkit.Tests/Parity/` (scenario source, fakes, comparer) and
`dotnet/tools/CMToolkit.ParityCoverage/` (the coverage check).

## Scenarios

### `tree/`

The fake machine's files, committed literally. `.gitattributes` marks trees `-text`, so line endings, BOMs and invalid
bytes reach both sides byte for byte. `.gitignore` un-ignores them, so an empty `Fallout4.exe` here is a fixture. By
convention the top-level folders are `Game/`, `Documents/`, `LocalAppData/`, `MO2/`, `Cwd/` and `App/` (the exe
folder), as the scenario needs. Don't add a `.gitkeep`: the Scanner would flag it. Use `emptyDirs` instead.

Both sides copy the tree to a fresh temp root named `cmt-parity-<guid>`, then apply the manifest extras. The root
must be on **NTFS**, because goldens compare directory enumeration order exactly. Set `CMT_PARITY_TEMP` to an NTFS
folder if the system temp folder isn't on NTFS (a ReFS Dev Drive, for example).

### `scenario.json`

```jsonc
{
  "description": "What the scenario sets up.",
  "operation": "download-source",          // what the driver runs, and what the C# test calls
  "parity": ["SET-2"],                     // Parity Inventory IDs this scenario proves
  "emptyDirs": ["App/assets"],             // tree-relative folders to create (git can't hold empty ones)
  "attributes": [                          // closed vocabulary: readonly, hidden, system
    {"path": "App/assets/download-source.txt", "set": ["readonly", "hidden"]}
  ],
  "host": { ... },                         // the fake OS around the tree, below
  "deviations": [                          // optional: reference bugs the port fixes here (see Deviations)
    {"bug": 77, "reason": "The Download Source line reaches cm-toolkit.log."}
  ]
}
```

Unknown keys are errors on both sides, so a typo fails instead of being ignored.

### The `host` block

It drives the driver's monkeypatches and the C# `FakeHostEnvironment`. Any string may start with `<ROOT>`, which
expands to the temp root. The rest of the string is kept as written, so use the separator the faked API returns
(`\\` in registry values, `/` in Tk dialog answers). Everything is optional. A fact the scenario doesn't give is
missing on the fake machine: the recording machine's real registry, environment and folders never leak in.

| Key | Fakes | Example |
|---|---|---|
| `appDir` | The exe folder: `sys._MEIPASS`, C# `AppContext.BaseDirectory`. Without it the driver uses `src/` (the reference's own assets). | `"<ROOT>/App"` |
| `cwd` | The working directory. Defaults to the root. | `"<ROOT>/Cwd"` |
| `argv0` | `sys.argv[0]` (OVW-P1). | `"cm-toolkit.exe"` |
| `env` | Environment variables (`os.getenv` in `game_info`). Only these exist. | `{"LOCALAPPDATA": "<ROOT>\\LocalAppData"}` |
| `knownFolders` | `SHGetFolderPathW`: `Documents`, `LocalAppData` (also `AppData`, `Desktop`). | `{"Documents": "<ROOT>\\Documents"}` |
| `registry` | `winreg`. Keys under `HKLM`/`HKCU`, matched case-insensitively. A parent of a listed key exists too. Value types: `REG_SZ`, `REG_EXPAND_SZ`, `REG_DWORD`, `REG_QWORD`. | `{"HKLM\\SOFTWARE\\WOW6432Node\\Bethesda Softworks\\Fallout4": {"Installed Path": {"type": "REG_SZ", "value": "<ROOT>\\Game"}}}` |
| `processes` | The parent-process chain from the parent up (`psutil.Process`, MM-1). Each entry has a `name`, plus an optional `exe` (omitting it raises `AccessDenied`, as an elevated parent does) and an optional `fileVersion` (the exe's version resource). | `[{"name": "ModOrganizer.exe", "exe": "<ROOT>/MO2/ModOrganizer.exe", "fileVersion": [2, 5, 2, 0]}]` |
| `os` | `platform.system()`, `platform.release()`, the build, and whether `ntdll` exports `wine_get_version`. | `{"system": "Windows", "release": "11", "build": 26100, "wine": false}` |
| `pc` | Total physical memory. | `{"ramBytes": 68719476736}` |
| `dialogs` | Answers for the dialogs that ask a question, in call order. A dialog with no answer left, or answers out of order, fails the recording, and so does an answer nothing asked for. Informing dialogs (`showerror`, `showwarning`, `showinfo`) need none. | `[{"function": "askyesno", "answer": true}, {"function": "askopenfilename", "answer": "<ROOT>/Game/Fallout4.exe"}]` |

`IHostEnvironment` in Core only has the members slices have needed so far. A slice that reads more of the host adds
the member there and in `FakeHostEnvironment`. The host block schema is already parsed and validated on both sides.

### `http/`

Canned responses, keyed by **logical resource** rather than URL, because where the C# app downloads Delta Patches from
is a deployment detail ([#16](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/16)):

| Resource | URL it stands for |
|---|---|
| `nexus-page` | `https://www.nexusmods.com/fallout4/mods/87907` (NET-1) |
| `github-latest-release` | `https://api.github.com/repos/<owner>/<repo>/releases/latest` (NET-2) |
| `delta/<file>.xdelta` | any URL whose last segment is that file (NET-3) |

Each resource is `http/<resource>.response.json`, holding either `{"status": 200, "headers": {...}}` or
`{"failure": "timeout" | "connection"}`, plus an optional `http/<resource>.body` with the raw bytes. A body has a
`Content-Length` only if the headers declare one (B-10 depends on its absence). A request with no canned response fails
the recording and the C# test. Responses on the Python side are real `requests.Response` objects over the canned bytes.
The C# side is `ParityHttpHandler`, an `HttpMessageHandler` for an injected `HttpClient`.

## Goldens

`golden.json` is written by the driver, never by hand. (A deviating scenario's `golden.port.json` is the one exception;
see [Deviations](#deviations-fixing-a-reference-bug).) It is a neutral JSON projection: canonical (sorted keys, ASCII
escapes, LF), with:

- **Semantic values**: what Core returns. Enums are camelCase strings, such as `"readFailed"`.
- **`display` sub-objects**: the text the reference renders from those values (labels, log lines, message text). Core
  tests compare in `GoldenCompareMode.Semantic`, which skips every `display`. App presenter tests compare in `Full`.
- **Paths as `<ROOT>` tokens**: `<ROOT>/Game/Data/a.esp`. Inside rendered text, the root is replaced in place.
  Where the exact `str(path)` matters (MO2 paths, T-7), the `mod-manager` operation keeps the backslashed text with the
  root replaced in place, and writes a path outside the root on the root's drive as `<DRIVE>\...` (B-6). Absolute
  paths in tree files use drive `X:` so they never collide with the temp root's drive.
- **Unordered arrays** as `{"$unordered": [...]}`, compared as multisets. Use them only for hash-ordered dimensions
  (T-15, B-4) and fields the reference genuinely doesn't order. Directory enumeration order is compared exactly.
- `dialogs` and `httpRequests`: every dialog shown and every request made, when there are any.

The C# test builds the same projection from its own results and calls `GoldenAssert.Matches`. Numbers compare by
value, so Python's `1.0` matches C#'s `1`. The rendered `1.0` (T-3) belongs in a `display` string.

## Recording (developer machine only)

The driver needs Windows, an NTFS temp folder, and **Python 3.14 with Tk 8.6**: the reference's bundled sv_ttk refuses
Tk 9, and widget-level results could differ under it. python.org's 3.14 installers ship Tk 8.6. uv's managed builds
ship Tk 9, so point uv at a python.org interpreter:

```sh
uv venv --python "C:\path\to\python.org\python.exe"
uv sync
uv run python parity/driver/record.py                    # record every scenario
uv run python parity/driver/record.py "settings/*"       # a glob over <module>/<scenario> IDs
uv run python parity/driver/record.py --check            # fail if any golden would change
uv run python -m unittest discover -s parity/driver/tests -t parity/driver   # the driver's own tests
```

Each scenario runs in a fresh child process with `PYTHONHASHSEED=0`. The driver imports the Reference Implementation
**unmodified**. It forces `win11_24h2 = False`, so goldens record plain existence semantics (the .NET exemption) on any
Windows build. It also replaces OS-facing names **where they are used** (`helpers.winreg`, `game_info.messagebox`,
`tabs._overview.get_file_version`, ...). See `driver/harness/reference.py` for the full table. In record mode it creates
one withdrawn Tk root, keeps `ModalWindow`s hidden and ungrabbed, and offers a duck-typed `cmc` stub (`Session.make_cmc`)
for operations that drive a single tab or window.

To add an operation, register a function in `driver/harness/operations.py`. It takes the started `Session` and a
`GoldenContext`, and returns the golden dict. Read results that only exist after the GUI is built off the widgets.

Goldens are recorded once, at the Parity Baseline. Re-recording later happens only through a "mirror this change"
issue, before cutover.

## Replaying in C#

```csharp
public static TheoryData<ParityScenario> Scenarios => ParityScenarios.Of("settings", "download-source");

[Theory]
[MemberData(nameof(Scenarios))]
public void Core_matches_the_reference(ParityScenario scenario)
{
    using var machine = scenario.Materialize();          // NTFS temp copy + extras; deleted on dispose
    var lookup = DownloadSourceFile.Read(machine.Host.AppDirectory);
    GoldenAssert.Matches(scenario.LoadGolden(), Project(lookup), GoldenCompareMode.Semantic);
}
```

`CommittedScenarioTests` fails if a committed scenario is unrecorded, doesn't parse, or isn't picked up by any
`[MemberData]` returning `TheoryData<ParityScenario>`. A scenario only proves its IDs if a test replays it.

`LoadGolden()` returns `golden.port.json` for a scenario that declares `deviations`, and `golden.json` otherwise, so a
replaying test never needs to know whether its scenario deviates. `LoadReferenceGolden()` always returns the recording.

## Deviations (fixing a reference bug)

The port fixes the Reference Implementation's `bug` issues instead of reproducing them
([ADR-0006](../docs/adr/0006-port-fixes-reference-bugs-as-deviations.md)). Each fix is a **Deviation**: an explicit
override of the recording, named by its bug issue number and a one-line reason. The recording is never edited.
`golden.json` stays what the unmodified reference produced, and `record.py --check` keeps verifying it. The slices
record parity faithfully first, so the scenarios and tests that prove a bug name its number (`#77`, `Bug #79`, ...);
search for it to find what a fix flips.

**Flipping a scenario** that recorded the bug:

1. Add `"deviations": [{"bug": <n>, "reason": "<what the port does instead>"}]` to its `scenario.json`. The bug is a
   positive integer and the reason isn't blank. List several entries if fixes to several bugs change this scenario,
   one per bug.
2. Copy `golden.json` to `golden.port.json` beside it, and change only the fields the fix changes. Keep the golden
   format (sorted keys, two-space indent, LF). The port golden is the one golden written by hand, because no reference
   produces it.
3. Leave `golden.json`, `tree/`, `http/` and the `parity` list alone. The scenario still proves its IDs, now with the
   fixed behaviour.
4. Run the replaying test (red until the fix lands, then green), `record.py --check` (the scenario reads `same` and is
   marked `port deviates: #<n>`), and the coverage check.

If the fix needs a new input to show the corrected behaviour, add a **new** scenario as usual. It is recorded from the
reference like any other, then flipped as above.

**A hand-written test that asserted the buggy behaviour** (a `[Fact]`/`[Theory]` with `[Trait("Parity", ...)]`):

- Change its assertions to the fixed behaviour, and rename it if its name described the bug.
- Keep its `Trait("Parity", "<ID>")`, and add `Deviation(<n>, "<reason>")` from `CMToolkit.Tests.Parity` to the same
  attribute list. Write the bug as an integer literal and the reason as a regular string literal, because the coverage
  check reads it as text. It is also an xUnit trait, so `dotnet test --filter "Deviation=<n>"` runs every test the fix
  touched.
- Rewrite the comments that say "reproduced, not fixed" or cite ADR-0001, so they name the bug as fixed.

The checks enforce the pairing. `LoadGolden()` (and therefore `CommittedScenarioTests`) and the coverage check both fail
on a declared deviation with no `golden.port.json`, and on a `golden.port.json` with no declared deviation. The coverage
check also fails on a malformed bug number, a missing or blank reason, an empty `deviations` list, and a port golden
identical to the recording. It lists every deviation on each run.

## Coverage check

`dotnet run --project dotnet/tools/CMToolkit.ParityCoverage` runs in CI after the tests. It collects proofs from:

- each recorded scenario's `parity` list;
- every `[Trait("Parity", "<ID>")]` in `dotnet/tests/` (found as text, so don't write one inside a string literal);
- `coverage-manual.json` entries: `{"id": "...", "proof": "exempt" | "manual" | "screenshot", "reason": "..."}`.

It **fails** when an ID in `covered.txt` has no proof, when a proof or `covered.txt` cites an ID that isn't in the
inventory, when a scenario has no golden, or when a [deviation](#deviations-fixing-a-reference-bug) is malformed. It
reports the unproven IDs and the proven IDs not yet in `covered.txt`, and lists every deviation from scenarios and from
`Deviation` attributes in `dotnet/tests/`.
With `--strict`, which the Verify full parity slice switches on, it also fails on every inventory ID that is unproven
or missing from `covered.txt`.

A slice adds the IDs it proves to `covered.txt` when it merges. An ID that several slices prove in part (*part* in
the slice's issue) is added when the last of them merges. That is why `SHELL-7` has proofs but isn't ratcheted yet.

## Screenshots

```sh
dotnet publish dotnet/src/CMToolkit.App -c Release -r win-x64 -o dotnet/artifacts/publish
uv run python parity/driver/screenshots.py shell/main-window --slice shell --crop tab-strip=0,0,-1,80
```

The script runs the unmodified `src/main.py` with the scenario's host fakes, and the published exe in the scenario's
`cwd`, each on its own copy of the tree. It captures both windows at 100 % DPI and writes `tk.png`, `avalonia.png`,
`compare.png` (side by side plus a 50/50 blend) and a `compare-<name>.png` per crop to `screenshots/<slice>/`. A human
approves the pair in the slice PR. There is no automated pixel diff, and no capture in CI.

`--tab <Name>` captures another tab than the Overview. The reference selects it in-process, two seconds after
`CMChecker` is built (Tk reads the real mouse-button state, so a posted click doesn't reach it); the exe gets a posted
click on the tab's header, whose position is fixed in the script's `TAB_CENTRES`. The Tools, Settings and About pairs
reuse `shell/main-window`, whose `settings.json` the Settings tab shows (`update_source` none, `log_level` INFO).

`--modal <Name>` captures a modal opened from the Overview instead of the main window (today only `Downgrader`). The
reference opens it in-process two seconds after `CMChecker` is built (told which by `CMT_SCREENSHOT_MODAL`); the exe
gets a posted click on the opening button, fixed in `MODAL_BUTTONS`. The capture is the window with that title.

`--tab Scanner --scan` runs an Overview Issues scan before capturing: the reference unticks the six data boxes and
presses Scan Game in-process (told by `CMT_SCREENSHOT_SCAN`), the exe gets posted clicks at `SCAN_CLICKS` in its side
pane. `--panes` pastes the app's other windows (the Scanner's docked panes) where they sit around the main window, on
black, so the capture shows them in place.

Both sides see the whole `host` block. The exe starts in `cwd` with `CMT_PARITY_SCENARIO` (the `scenario.json`) and
`CMT_PARITY_ROOT` (its copy of the tree) set, and then runs on `ScenarioHostEnvironment`
(`dotnet/src/CMToolkit.App/Parity/`) instead of the real machine: the same rules as the tests' `FakeHostEnvironment`,
which `ScenarioHostEnvironmentTests` checks on every committed scenario. Two differences: with no `appDir` the exe uses
its own folder, and `dialogs` aren't scripted on the exe side (it shows real ones), so screenshot scenarios should find
the game without asking. The smoke test (`dotnet/eng/Test-ShellStarts.ps1 -Scenario <folder>`) uses the same variables.

The scenario's `http/` folder reaches both sides too: the driver's `FakeRequests` serves the reference, and the exe's
update check goes through `ScenarioHttpHandler` (same folder, same rules as the tests' `ParityHttpHandler`, checked by
`ScenarioHttpHandlerTests`). A request the scenario has no response for fails startup rather than going online, so a
screenshot scenario whose `settings.json` has an `update_source` other than `none` needs its responses
(`update/banner-window` is the example).
