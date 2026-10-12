namespace CMToolkit.Core.Updates;

/// <summary>
/// A connection stream on which every single read and write must finish within a timeout, as a Python socket with
/// <c>settimeout</c> behaves (T-14): <c>requests.get(..., timeout=5)</c> limits each socket operation, not the whole
/// request, so a slow but steady download never times out, while five seconds of silence always does.
/// <see cref="HttpClient.Timeout"/> is a total, so the update check's client turns it off and wraps its sockets in this.
/// </summary>
/// <param name="inner">The connection's stream; owned, and disposed with this one.</param>
/// <param name="timeout">The longest any one read or write may take.</param>
public sealed class ReadTimeoutStream(Stream inner, TimeSpan timeout) : Stream
{
    /// <inheritdoc/>
    public override bool CanRead => inner.CanRead;

    /// <inheritdoc/>
    public override bool CanWrite => inner.CanWrite;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc/>
    /// <exception cref="TimeoutException">Nothing arrived within the timeout.</exception>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var timer = Start(cancellationToken);
        try
        {
            return await inner.ReadAsync(buffer, timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No data was received for {timeout.TotalSeconds:0.###} s.");
        }
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    /// <exception cref="TimeoutException">The data couldn't be sent within the timeout.</exception>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var timer = Start(cancellationToken);
        try
        {
            await inner.WriteAsync(buffer, timer.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The data couldn't be sent within {timeout.TotalSeconds:0.###} s.");
        }
    }

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    /// <remarks>Synchronous calls go to the inner stream, whose own timeouts apply (see <c>CreateHandler</c>).</remarks>
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override void Flush() => inner.Flush();

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>A fresh timer for one operation, also cancelled by the caller's token.</summary>
    private CancellationTokenSource Start(CancellationToken cancellationToken)
    {
        var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timer.CancelAfter(timeout);
        return timer;
    }
}
