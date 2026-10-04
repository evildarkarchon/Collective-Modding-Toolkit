# Behaviour Parity is proved by recorded Parity Scenarios against an untouched reference

C# Core proves Behaviour Parity by reproducing **Parity Scenarios**. Each scenario is a fake machine: a game tree, Documents, LocalAppData, MO2 and CWD, plus a host description covering registry values, the parent-process chain, known folders, OS strings, dialog answers and canned HTTP responses. Each also carries golden results that the Reference Implementation produced for it at the Parity Baseline.

**Recording**
- A Python driver records the goldens once, on a developer machine.
- It imports the Reference Implementation **unmodified**. Headless behaviour comes only from monkeypatching (winreg, psutil, Win32 helpers, `messagebox`/`filedialog`, `requests`, `ModalWindow` show/grab), a withdrawn Tk root and a duck-typed `cmc` stub.
- Results that the reference only computes while building widgets are read back off the widgets. These are the per-binary and limit problems, and the F4SE verdicts in the `Treeview`.

**Comparison**
- Goldens are committed. They are a neutral JSON projection of the results: semantic values plus a `display` sub-object for rendered text, with paths as `<ROOT>`-relative tokens.
- C# tests compare against them exactly. Two kinds of field are the exception and are compared as multisets: hash-ordered sets (T-15/B-4) and fields marked unordered.
- **CI never runs Python.**

## Considered Options

- **Live differential testing**, running Python and C# side by side in CI. The baseline is frozen, so every run would return the same answer. The Python tree is deleted at cutover. And CI would need Python 3.14, Tk and registry shims for no new information.
- **Refactoring the reference for testability**: extracting logic out of `_build_gui`, removing message boxes mid-logic. Any edit makes the golden a recording of edited code, which defeats the oracle. Paths the driver can't reach are covered by hand-written tests that cite Parity Inventory IDs.
- **Committing real game binaries** for Install Type and Downgrader scenarios. That is copyrighted, large and unnecessary. Version-resourced stubs and F4SE DLLs are built from committed C/`.rc` sources with MSVC. Downgrader inputs are CRC32-forged small files, and their Delta Patches are self-made xdelta3 diffs served by the HTTP fake.

## Consequences

- Goldens outlive the Reference Implementation. After cutover they are ordinary regression fixtures, and the driver can no longer regenerate them. Re-recording happens only through a "mirror this change" issue before cutover.
- Recording forces `win11_24h2 = False` and `PYTHONHASHSEED=0`. Goldens record plain existence semantics, which match the .NET exemption, and don't depend on the recording machine's build.
- Directory enumeration order is compared exactly, so scenario temp roots must be on NTFS.
- Goldens cover **fixed inputs only**. Timing-dependent interaction (mid-download option toggles, a tab switch mid-scan, ADR-0003 input blocking) and App behaviour are hand-written Core or Avalonia.Headless tests that cite Parity Inventory IDs.
- The Parity Inventory moves to `main`. A coverage check fails while any ID has no scenario, test, or recorded manual/screenshot proof.
