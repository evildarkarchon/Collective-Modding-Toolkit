using CMToolkit.App.Runtime;

namespace CMToolkit.Tests.Support;

/// <summary>An <see cref="IBrowser"/> that opens nothing and keeps every URL it was asked to open, in order.</summary>
public sealed class RecordingBrowser : IBrowser
{
    private readonly List<string> _opened = [];

    /// <summary>Every URL opened so far.</summary>
    public IReadOnlyList<string> Opened => _opened;

    /// <inheritdoc/>
    public bool Open(string url)
    {
        _opened.Add(url);
        return true;
    }
}
