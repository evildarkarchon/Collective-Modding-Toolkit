namespace CMToolkit.Core;

/// <summary>
/// How much of the UI an operation leaves live (ADR-0003). It classifies each operation by what the Reference
/// Implementation did at the Parity Baseline. A long Core operation that switches between the two, the Downgrader's
/// run, reports the switch as a phase marker, and the App maps it onto the same rule.
/// </summary>
public enum OperationPhase
{
    /// <summary>
    /// The reference ran this on the Tk UI thread, freezing the app. Input to the main window and any open modal is
    /// discarded until it ends; Close and Escape are deferred, then honoured.
    /// </summary>
    Blocking,

    /// <summary>The reference ran this on a worker thread, so the UI stays live, as it did there.</summary>
    Interactive,
}
