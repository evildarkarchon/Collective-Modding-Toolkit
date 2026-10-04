# Ship as a NativeAOT build in the upstream archive layout

The C# app is published as a **NativeAOT** `win-x64` exe so that it starts fast, needs no installed .NET runtime, and avoids the self-extraction behaviour that antivirus heuristics flag. The cost is that every line of Core and App must stay trim/AOT-clean: no reflection-heavy libraries, no built-in COM (so no `System.Management`/WMI), and P/Invokes via `[LibraryImport]`. Trim/AOT warnings are errors. We fall back to self-contained single-file only if a required dependency emits a trim/AOT warning that can be neither fixed nor suppressed with a documented justification, and making that switch reopens this decision.

The release keeps the upstream 0.6.x shape so it can be extracted over an existing install: `cm-toolkit.7z` with everything under `Tools\CM-Toolkit\` (the exe and its `.pdb`, Avalonia's native DLLs, `assets\download-source.txt`, `LICENSE.md`, `THIRD-PARTY-NOTICES.txt`). One build is packaged twice, once per Download Source, and the two archives differ only in that text file. Images and the font are embedded in the exe. `download-source.txt` is resolved against the exe folder, while `settings.json`, `cm-toolkit.log` and downloaded `.xdelta` patches stay **CWD-relative**, as in the Reference Implementation. The exe manifest mirrors today's (`asInvoker`, `longPathAware`) plus the Windows 10 `supportedOS` entry that `FileVersionInfo` needs. The version comes from a single `<Version>` read back via `AssemblyInformationalVersion` with the source-revision suffix disabled. The build is unsigned, like upstream.

## Considered Options

- **Self-contained single-file**: kept as the fallback. It is larger, starts slower, and extracting native libraries to `%TEMP%` is an antivirus red flag.
- **Self-contained folder**: closest to PyInstaller's onedir layout, but it has the biggest download and hundreds of loose files.
- **Framework-dependent**: rejected, because requiring users to install the .NET Desktop Runtime is too much friction for this audience.
- **Anchoring `settings.json` and the log to the exe folder**: rejected under Behaviour Parity. The CWD dependence is logged as a bug instead.
