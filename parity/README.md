# Scenario fixtures

Each **Parity Scenario** is a fake machine: a game tree plus the OS around it. During the port, a Python driver
recorded once what the Python app did on each one (the *golden*), and C# tests replay the scenario against Core or the
App and compare. That proved the port matched the Python app
([ADR-0004](../docs/adr/0004-parity-proved-by-recorded-scenarios.md)); the port is done, and the parity process is
retired ([ADR-0007](../docs/adr/0007-parity-process-retired.md)).

The scenarios and goldens stay as **regression fixtures**: the tests below still replay them on every run. The
driver is gone (preserved at the `python-reference` tag), so a golden can't be re-recorded. Cover new behaviour with
ordinary hand-written tests, and when a fix or feature changes what a scenario must produce, edit its golden
([below](#changing-a-scenarios-expected-result)).

## Layout

| Path | What it is |
|---|---|
| `scenarios/<module>/<scenario>/` | One scenario: `tree/`, `scenario.json`, optional `http/`, `golden.json`, and a `golden.port.json` where the port's fix of a Python bug changed the result ([below](#changing-a-scenarios-expected-result)). |
| `assets/` | Sources and outputs for synthetic binaries (version stubs, F4SE DLLs, CRC32-forged files, deltas). |
| `screenshots/<slice>/` | Screenshot pairs against the Tk app, captured during the port. The README's screenshots come from here. |

The C# side lives in `dotnet/tests/CMToolkit.Tests/Parity/` (scenario source, fakes, comparer).

## Scenarios

### `tree/`

The fake machine's files, committed literally. `.gitattributes` marks trees `-text`, so line endings, BOMs and invalid
bytes reached the driver and reach the C# tests byte for byte. `.gitignore` un-ignores them, so an empty `Fallout4.exe` here is a fixture. By
convention the top-level folders are `Game/`, `Documents/`, `LocalAppData/`, `MO2/`, `Cwd/` and `App/` (the exe
folder), as the scenario needs. Don't add a `.gitkeep`: the Scanner would flag it. Use `emptyDirs` instead.

The tests copy the tree to a fresh temp root named `cmt-parity-<guid>`, then apply the manifest extras. The root
must be on **NTFS**, because goldens compare directory enumeration order exactly. Set `CMT_PARITY_TEMP` to an NTFS
folder if the system temp folder isn't on NTFS (a ReFS Dev Drive, for example).

An operation whose scenarios change files behind the app's back (the `archive-patcher` operation, after the Overview
has read the archives) applies the tree's `Changes/` overlay at that point: each file under `Changes/` replaces the
file at the same root-relative path, and an empty `<name>.delete` deletes `<name>`. Until then `Changes/` is just
another folder in the tree (`ArchivePatcherGolden.ApplyChanges`).

An operation that clicks through a window before its run reads the clicks from the tree's `Script/<operation>.json`.
The `downgrader-run` operation reads `Script/downgrader-run.json`: `desiredVersion` (the radio to click,
`"Old-Gen"`/`"Next-Gen"`), `keepBackups` and `deletePatches` (the value to click each box to), each optional; unknown
keys are errors. Nothing in the app reads `Script/`, and the golden echoes the script it ran.

### `scenario.json`

```jsonc
{
  "description": "What the scenario sets up.",
  "operation": "download-source",          // what the driver ran, and what the C# test calls
  "parity": ["SET-2"],                     // the port-era checklist IDs it was recorded for (historical labels)
  "emptyDirs": ["App/assets"],             // tree-relative folders to create (git can't hold empty ones)
  "attributes": [                          // closed vocabulary: readonly, hidden, system
    {"path": "App/assets/download-source.txt", "set": ["readonly", "hidden"]}
  ],
  "host": { ... },                         // the fake OS around the tree, below
  "deviations": [                          // optional: where the port departs from the recording (golden.port.json)
    {"bug": 77, "reason": "The Download Source line reaches cm-toolkit.log."}
  ]
}
```

Unknown keys are errors (as they were in the driver), so a typo fails instead of being ignored. So is a key repeated in any object,
which JSON parsers otherwise resolve by keeping the last one.

### The `host` block

It drove the driver's monkeypatches, and drives the C# `FakeHostEnvironment`. Any string may start with `<ROOT>`, which
expands to the temp root. The rest of the string is kept as written, so use the separator the faked API returns
(`\\` in registry values, `/` in Tk dialog answers). Everything is optional. A fact the scenario doesn't give is
missing on the fake machine: the real registry, environment and folders never leak in.

| Key | Fakes | Example |
|---|---|---|
| `appDir` | The exe folder: `sys._MEIPASS`, C# `AppContext.BaseDirectory`. Without it the driver used the reference's own assets folder. | `"<ROOT>/App"` |
| `cwd` | The working directory. Defaults to the root. | `"<ROOT>/Cwd"` |
| `argv0` | `sys.argv[0]` (OVW-P1). | `"cm-toolkit.exe"` |
| `env` | Environment variables (`os.getenv` in `game_info`). Only these exist. | `{"LOCALAPPDATA": "<ROOT>\\LocalAppData"}` |
| `knownFolders` | `SHGetFolderPathW`: `Documents`, `LocalAppData` (also `AppData`, `Desktop`). | `{"Documents": "<ROOT>\\Documents"}` |
| `registry` | `winreg`. Keys under `HKLM`/`HKCU`, matched case-insensitively. A parent of a listed key exists too. Value types: `REG_SZ`, `REG_EXPAND_SZ`, `REG_DWORD`, `REG_QWORD`. | `{"HKLM\\SOFTWARE\\WOW6432Node\\Bethesda Softworks\\Fallout4": {"Installed Path": {"type": "REG_SZ", "value": "<ROOT>\\Game"}}}` |
| `processes` | The parent-process chain from the parent up (`psutil.Process`, MM-1). Each entry has a `name`, plus an optional `exe` (omitting it raises `AccessDenied`, as an elevated parent does) and an optional `fileVersion` (the exe's version resource). | `[{"name": "ModOrganizer.exe", "exe": "<ROOT>/MO2/ModOrganizer.exe", "fileVersion": [2, 5, 2, 0]}]` |
| `os` | `platform.system()`, `platform.release()`, the build, and whether `ntdll` exports `wine_get_version`. | `{"system": "Windows", "release": "11", "build": 26100, "wine": false}` |
| `pc` | Total physical memory. | `{"ramBytes": 68719476736}` |
| `dialogs` | Answers for the dialogs that ask a question, in call order. A dialog with no answer left, or answers out of order, fails the recording, and so does an answer nothing asked for. Informing dialogs (`showerror`, `showwarning`, `showinfo`) need none. | `[{"function": "askyesno", "answer": true}, {"function": "askopenfilename", "answer": "<ROOT>/Game/Fallout4.exe"}]` |

`IHostEnvironment` in Core only has the members the app needs. Code that reads more of the host adds the member there
and in `FakeHostEnvironment`.

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
`Content-Length` only if the headers declare one (B-10 depends on its absence). A request with no canned response failed
the recording and fails the C# test. The driver served real `requests.Response` objects over the canned bytes; the C#
side is `ParityHttpHandler`, an `HttpMessageHandler` for an injected `HttpClient`.

The `update/*` goldens were recorded with the reference running `0.6.2-dev`, and which canned release counts as an
update depends on that. Their replays pass `ParityScenario.RecordedAppVersion` to the update check instead of the app's
own version, so a version bump doesn't change them.

## Goldens

`golden.json` was written by the driver; a `golden.port.json` was written by hand during the port. Both are now
fixtures that can be edited ([below](#changing-a-scenarios-expected-result)). A golden is a neutral JSON projection:
canonical (sorted keys, ASCII escapes, LF), with:

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

## How the goldens were recorded

The driver (`parity/driver/` at the `python-reference` tag) ran on a developer machine with Python 3.14 and Tk 8.6.
Each scenario ran in a fresh child process with `PYTHONHASHSEED=0`. The driver imported the Reference Implementation
**unmodified** and replaced OS-facing names where they were used (`helpers.winreg`, `game_info.messagebox`,
`tabs._overview.get_file_version`, ...). It forced `win11_24h2 = False`, so goldens record plain existence semantics
(the .NET exemption) whatever the recording machine's Windows build. Results the reference only computed while building
widgets were read back off the widgets.

Goldens were recorded once, during the port. The driver was removed with the Python app at 0.7.0, so no golden can be
recorded or re-recorded now.

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
`[MemberData]` returning `TheoryData<ParityScenario>`. A scenario no test replays protects nothing.

`LoadGolden()` returns `golden.port.json` for a scenario that declares `deviations`, and `golden.json` otherwise, so a
replaying test never needs to know whether its scenario deviates. `LoadReferenceGolden()` always returns the recording.

## Changing a scenario's expected result

When a bug fix or a feature changes what a scenario must produce, edit the golden its replay reads, in the same
change as the code: `golden.port.json` if the scenario has one, otherwise `golden.json`. Change only the fields the
change affects, and keep the golden format (sorted keys, two-space indent, LF). Leave `tree/`, `http/` and the
`parity` list alone unless the change needs a different input; if it does, a hand-written test is usually simpler
than reshaping a scenario.

`LoadGolden()` checks that `golden.port.json` and `deviations` come together: either one without the other fails
`CommittedScenarioTests`. So don't add or remove either one on its own. The existing `deviations` entries and `Deviation` test attributes are the port's
record of the Python bugs it fixed (ADR-0006); keep them, but new changes don't need new ones.

## Screenshots

The pairs under `screenshots/<slice>/` were captured during the port by the driver's `screenshots.py` (at the
`python-reference` tag). It ran the unmodified reference with the scenario's host fakes and the published exe in the
scenario's `cwd`, each on its own copy of the tree, captured both windows at 100 % DPI, and wrote `tk.png`,
`avalonia.png`, `compare.png` (side by side plus a 50/50 blend) and a `compare-<name>.png` per crop. A human approved
each pair in its slice PR. They are a record now: the Tk side can't be captured again. Their title bars read
`v0.6.2-dev`, the version both sides ran as then.

### Running the exe on a scenario's machine

The published exe can still run on a Parity Scenario's fake machine, which the CI smoke test does. It sees the whole
`host` block. The exe starts in `cwd` with `CMT_PARITY_SCENARIO` (the `scenario.json`) and
`CMT_PARITY_ROOT` (its copy of the tree) set, and then runs on `ScenarioHostEnvironment`
(`dotnet/src/CMToolkit.App/Parity/`) instead of the real machine: the same rules as the tests' `FakeHostEnvironment`,
which `ScenarioHostEnvironmentTests` checks on every committed scenario. Two differences: with no `appDir` the exe uses
its own folder, and `dialogs` aren't scripted on the exe side (it shows real ones), so a scenario the exe runs on
should find the game without asking. The smoke test (`dotnet/eng/Test-ShellStarts.ps1 -Scenario <folder>`) uses the same variables.
The app keeps `settings.json`, `cm-toolkit.log` and Delta Patches in its own folder, not the working directory (bug
[#36](https://github.com/evildarkarchon/Collective-Modding-Toolkit/issues/36), fixed). On a scenario's machine that
folder is `cwd` (`AppFolder.ForThisRun`), where scenario trees keep the reference's files, so the exe runs on the
scenario's settings and never writes beside the published exe.

The scenario's `http/` folder reaches the exe too: its update check goes through `ScenarioHttpHandler` (same folder,
same rules as the tests' `ParityHttpHandler`, checked by `ScenarioHttpHandlerTests`). A request the scenario has no
response for fails startup rather than going online, so a scenario whose `settings.json` has an `update_source` other
than `none` needs its responses (`update/banner-window` is the example). The exe compares the canned releases against
its own version, not the recorded `0.6.2-dev`, so on `update/banner-window` it now shows only the GitHub `v0.7.1`.
