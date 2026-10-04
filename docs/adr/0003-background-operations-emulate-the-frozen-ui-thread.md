# Background operations emulate the reference's frozen UI thread

The Reference Implementation runs almost everything synchronously on the Tk UI thread: the startup update check, Overview load and refresh, F4SE DLL loading, the Archive Patcher, and the Downgrader's version detection and xdelta decode. Only the Scanner's data walk and the Downgrader's delta downloads run on threads. The frozen UI thread was an accidental mutex: while it was blocked, nobody could refresh, switch tabs, double-click a button or close a window. Several observable results depend on that.

The C# app runs **every** operation through the App's background-operation runner, so the window never stops responding. It replaces the accidental mutex with a deliberate rule:

- **Blocked in the reference → input-blocked here.** Work that blocked the UI thread runs off-thread while the App blocks input to the main window and any open modal. Windows still paint, move and minimise. Clicks and keys are discarded. Close and Escape are deferred and honoured once the blocking work ends, the way Windows queued them for the frozen Tk loop.
- **Threaded in the reference → same interactivity here.** The Scanner's data walk and the delta downloads leave the UI live, exactly as in the reference. That includes the Failure Outcomes the reference can reach, such as switching away from the Scanner mid-scan, and toggling Downgrader options mid-download, which changes what later steps do.

Core stays synchronous and never knows about threads, except for the network-bound update check and delta download. Its one concession is the Downgrader, whose single run switches between blocking and interactive phases. Core owns that whole run sequence and emits `Blocking` / `Interactive` phase markers, and the App maps them onto the rule above. The startup update check is awaited before the main window is shown, so the update banner never appears after the window does.

## Considered Options

- **Mirror the reference's threading** (block the Avalonia UI thread wherever Tk was blocked): parity for free, but it keeps "Not Responding" freezes for multi-second CRC32 and DLL work.
- **Move work off-thread with no input blocking**: a responsive UI, but every newly reachable interleaving (refresh during refresh, scan during refresh, closing mid-patch) would need its own guard and its own parity argument.

## Consequences

- Known deviations, not parity items: input during a blocking phase is dropped rather than replayed. The process exits as soon as the window closes, where the reference's non-daemon threads kept it alive until a scan or download finished. Nondeterministic races (B-2, the scan results dropped by the completion flag) are not reproduced.
- Any new operation must be classified as blocking or interactive, using the reference's behaviour at the Parity Baseline.
