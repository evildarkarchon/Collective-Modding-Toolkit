namespace CMToolkit.Tests.Support;

/// <summary>
/// An <see cref="IProgress{T}"/> that records each value in <see cref="Reported"/>, in order, on the reporting thread.
/// <see cref="Progress{T}"/> would post them to the captured context instead, so a test reading them straight after the
/// call could miss some.
/// </summary>
/// <typeparam name="T">What is reported.</typeparam>
internal sealed class ListProgress<T> : IProgress<T>
{
    /// <summary>Every value reported so far, oldest first. Not thread-safe: report from one thread at a time.</summary>
    public List<T> Reported { get; } = [];

    /// <inheritdoc/>
    public void Report(T value) => Reported.Add(value);
}
