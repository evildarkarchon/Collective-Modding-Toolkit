# Collective Modding Toolkit

A Windows desktop tool for troubleshooting and optimizing Fallout 4 mod setups, built for the Collective Modding Discord community. This context also covers the effort to port the app from Python/tkinter to C#/Avalonia.

## Game installs

**Install Type**:
The edition of Fallout 4 the game folder contains — Old-Gen, Next-Gen, Anniversary, Down-Grade, and the not-found/unknown/obsolete states — as detected from the game binaries.
_Avoid_: game version (a specific executable build number, not the edition), OG/NG/AE as standalone nouns in prose

## The port

**Reference Implementation**:
The Python/tkinter app in `src/`, whose behaviour at the Parity Baseline defines what the C# app must do.
_Avoid_: old app, legacy version

**Parity Baseline**:
The frozen commit of the Reference Implementation the port is measured against; later Python changes reach the port only as explicit "mirror this change" issues.
_Avoid_: HEAD, latest Python

**Behaviour Parity**:
The C# app producing the same observable results as the Reference Implementation at the Parity Baseline — including its known bugs, which are logged separately rather than fixed in the port.
_Avoid_: feature parity (too loose — parity here covers behaviour, not just features)
