using CMToolkit.App.ViewModels;
using CMToolkit.Core.Scanner;

namespace CMToolkit.Tests.Support;

/// <summary>
/// A scripted <see cref="ScanState.RunScan"/> that holds mid-run until released, then hands back what
/// <c>then</c> makes of the snapshot it was given. Set <see cref="Run"/> as the module's <see cref="ScanState.RunScan"/>,
/// start a Scan, and the Scan stays running, past its Overview refresh, until <see cref="Release"/>.
/// </summary>
/// <remarks>
/// Dispose it (a <c>using</c>) so a failed test releases the scan and its worker doesn't wait out
/// <see cref="TaskTimeouts.Default"/>.
/// </remarks>
public sealed class HeldScan : IDisposable
{
    private readonly ManualResetEventSlim _finish = new();
    private readonly Func<ScanSnapshot, ScanOutcome> _then;

    // Written by the scan's worker and read on the UI thread, hence the volatile reads and writes.
    private bool _entered;

    /// <param name="then">
    /// What the scan returns once released, from its snapshot: <see cref="ScanOutcome.Empty"/> by default, or the real
    /// checks (<c>ScanChecks.Run</c>) for a Scan that lists results.
    /// </param>
    public HeldScan(Func<ScanSnapshot, ScanOutcome>? then = null) => _then = then ?? (_ => ScanOutcome.Empty);

    /// <summary>Whether the scan has started, past the Overview refresh, and is now held.</summary>
    public bool Entered => Volatile.Read(ref _entered);

    /// <summary>
    /// The scan, with <see cref="ScanState.RunScan"/>'s signature: marks itself entered, waits for
    /// <see cref="Release"/> (or <see cref="TaskTimeouts.Default"/>), then returns what <c>then</c> makes of
    /// <paramref name="snapshot"/>. It reports no status.
    /// </summary>
    /// <param name="snapshot">The Scan's snapshot.</param>
    /// <param name="progress">The Scan's status sink, unused.</param>
    public ScanOutcome Run(ScanSnapshot snapshot, IProgress<ScanStatus> progress)
    {
        Volatile.Write(ref _entered, true);
        _finish.Wait(TaskTimeouts.Default);
        return _then(snapshot);
    }

    /// <summary>Runs dispatcher jobs until the scan has started its checks, past the Overview refresh.</summary>
    public void WaitUntilEntered() => DispatcherWait.Until(() => Entered);

    /// <summary>Lets the scan finish.</summary>
    public void Release() => _finish.Set();

    /// <summary>Releases the scan, even when an assert failed, so the held scan's worker doesn't wait out its timeout.</summary>
    /// <remarks>
    /// The event is set, not disposed: a worker still inside <see cref="ManualResetEventSlim.Wait(TimeSpan)"/> would
    /// throw on a disposed one, and a slim event that never handed out its wait handle holds nothing to free.
    /// </remarks>
    public void Dispose() => _finish.Set();
}
