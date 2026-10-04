# Collective Modding Toolkit

A Windows desktop tool for troubleshooting and optimizing Fallout 4 mod setups, built for the Collective Modding Discord community. This context also covers the effort to port the app from Python/tkinter to C#/Avalonia.

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

## The port

**Reference Implementation**:
The Python/tkinter app in `src/`, whose behaviour at the Parity Baseline defines what the C# app must do.
_Avoid_: old app, legacy version

**Parity Baseline**:
The frozen commit of the Reference Implementation the port is measured against; later Python changes reach the port only as explicit "mirror this change" issues.
_Avoid_: HEAD, latest Python

**Behaviour Parity**:
The C# app producing the same observable results as the Reference Implementation at the Parity Baseline — including its known bugs, which are logged separately rather than fixed in the port. Failure Outcomes are part of it; how unhandled errors are surfaced is not.
_Avoid_: feature parity (too loose — parity here covers behaviour, not just features)

**Failure Outcome**:
What the app is left doing after it fails on bad input — a tab stuck on its loading text, a scan that never finishes, a file deleted or left behind — and the input that triggers it. Part of Behaviour Parity even when the outcome is a bug; the error type and its wording are not, unless the Reference Implementation wrote that message for the user.
_Avoid_: crash (the app usually keeps running), error (ambiguous with the message)

**Error Window**:
The "An Error Occurred" window that reports unhandled errors while the app runs. The port shows every unhandled error in it, and in the log, the moment it happens, where the Reference Implementation surfaced some late and others never.
_Avoid_: StdErr window (names the Python mechanism, not the thing), error dialog (ambiguous with the message boxes)
