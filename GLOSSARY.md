# Collective Modding Toolkit

A Windows desktop tool for troubleshooting and optimizing Fallout 4 mod setups, built for the Collective Modding Discord community. It was ported from Python/tkinter to C#/Avalonia in 0.7.0; the terms that port left behind are under Port history.

## Game installs

**Install Type**:
The edition of Fallout 4 the game folder contains — Old-Gen, Next-Gen, Anniversary, Down-Grade, and the not-found/unknown/obsolete states — as detected from the game binaries. Anniversary means the current Anniversary build, the one with the raised archive limits; earlier Anniversary-era builds are Obsolete.
_Avoid_: game version (a specific executable build number, not the edition), OG/NG/AE as standalone nouns in prose

**Obsolete**:
An Install Type for a superseded build that the toolkit recognises but doesn't support, such as an early Anniversary build without the raised archive limits.
_Avoid_: outdated, unsupported version (the Downgrader also calls Unknown files unsupported)

## Downgrading

**Delta Patch**:
A downloaded binary diff that turns one Install Type's game or Creation Kit file into another's. The Downgrader fetches one per file, as needed, from a fixed release.
_Avoid_: xdelta (that is the tool and file format, not the artifact), patch on its own (ambiguous with the Archive Patcher)

## Distribution

**Download Source**:
The site an archive was published to — Nexus Mods or GitHub — baked into that archive; it seeds the default Update Source in a fresh settings file.
_Avoid_: release channel (implies stable/beta tracks, which don't exist)

**Update Source**:
The user's setting for which site(s) the update check queries: Nexus Mods, GitHub, both, or none.
_Avoid_: Download Source (that is fixed per archive; this is the user's choice)

## Running the app

**Failure Outcome**:
What the app is left doing after it fails on bad input — a tab stuck on its loading text, a scan that never finishes, a file deleted or left behind — and the input that triggers it.
_Avoid_: crash (the app usually keeps running), error (ambiguous with the message)

**Error Window**:
The "An Error Occurred" window that reports unhandled errors while the app runs. The app shows every unhandled error in it, and in the log, the moment it happens, where the Reference Implementation surfaced some late and others never.
_Avoid_: StdErr window (names the Python mechanism, not the thing), error dialog (ambiguous with the message boxes)

**UI Action**:
Something set going in the window with nobody waiting on its result: a click, a box change, a tab switch, or a step the app schedules for itself, such as the Downgrader's next download. It stops at its first failure. The failure goes to the Error Window, and whatever the action skipped is its Failure Outcome.
_Avoid_: Tk callback (no Tk runs in the app), event handler (names the code that starts one, not the action)

## Port history

These terms come from the port from Python to C# (0.7.0). Code comments and older ADRs still use them; none of them constrains new work ([ADR-0007](docs/adr/0007-parity-process-retired.md)).

**Reference Implementation**:
The Python/tkinter app the C# app was ported from, as frozen at commit `f95a07c` for the port. It is preserved at the `python-reference` tag: useful for seeing how something used to behave, not a requirement.
_Avoid_: old app, legacy version

**Cutover**:
The 0.7.0 change that removed the Reference Implementation from the repo and made the C# app the product (#71).
_Avoid_: migration, launch, go-live

**Behaviour Parity**:
The port's goal: the same observable results as the Reference Implementation, except where a Deviation fixed a known bug. It was reached at the Cutover and is no longer checked.
_Avoid_: feature parity

**Parity Inventory**:
The port's checklist of the Reference Implementation's behaviours, each under an ID (`SHELL-5`, `OVW-12`, `T-15`, `B-4`) that comments and `Trait("Parity", ...)` still cite. It was removed after the port; read it with `git show 2b8cea1:docs/parity-inventory.md`.
_Avoid_: feature list, spec

**Parity Scenario**:
A fake game and mod-manager setup with the results recorded for it during the port, under `parity/scenarios/`. The scenarios are regression fixtures now: the tests replay them, and a change that alters a result edits the golden.
_Avoid_: test case (a scenario can back several tests), golden (that is the recorded result alone, not the setup)

**Deviation**:
A place where the port deliberately departed from the Reference Implementation, usually to fix one of its bugs, recorded by issue number in a scenario's `deviations` or a test's `Deviation` attribute. The existing ones are kept as a record; new changes don't add them.
_Avoid_: divergence, regression (both suggest an accident)
