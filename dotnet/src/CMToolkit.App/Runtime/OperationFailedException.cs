namespace CMToolkit.App.Runtime;

/// <summary>
/// A background operation died; its exception has already gone to the Error Window and the log. Callers let it
/// propagate, so the rest of their UI code is skipped as the rest of a Tk callback was, and the global handlers drop it
/// rather than report it twice.
/// </summary>
public sealed class OperationFailedException : Exception
{
    /// <param name="operationName">The operation's name, as given to <see cref="BackgroundOperations"/>.</param>
    /// <param name="innerException">What the work threw.</param>
    public OperationFailedException(string operationName, Exception innerException)
        : base($"Background operation '{operationName}' failed; the error has been reported.", innerException)
    {
    }
}
