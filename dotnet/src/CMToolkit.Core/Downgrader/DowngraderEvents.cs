namespace CMToolkit.Core.Downgrader;

/// <summary>
/// The Patch All button's state, where the run sets it: <c>button_patch.configure(state=NORMAL)</c> after the file loop
/// when its last file didn't need patching (B-11), and when the queue empties.
/// </summary>
/// <param name="Enabled">Whether the button is now enabled.</param>
public sealed record PatchAllEnabled(bool Enabled) : OperationEvent;

/// <summary>
/// Whether the patch queue is being worked through: <c>download_or_patch_in_progress</c> together with the window's
/// close guard <c>processing_data</c>, which the reference always set and cleared together. While it's true, the window
/// can't be closed (DG-6).
/// </summary>
/// <param name="Running">Whether the queue is now running.</param>
public sealed record QueueRunning(bool Running) : OperationEvent;

/// <summary>
/// The six files' versions, detected again once the queue has emptied (<c>get_info</c>), for the window to redraw its
/// labels (<c>draw_versions</c>).
/// </summary>
/// <param name="Versions">The versions in table order, as <see cref="DowngraderDetection.Detect"/> returns them.</param>
public sealed record VersionsDetected(IReadOnlyList<DowngraderFileVersion> Versions) : OperationEvent;
