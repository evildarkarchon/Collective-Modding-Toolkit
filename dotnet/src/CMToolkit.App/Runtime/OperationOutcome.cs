namespace CMToolkit.App.Runtime;

/// <summary>
/// How an Operation started through <see cref="UiActions.TryRun{T}"/> ended: <see cref="Succeeded"/> with its value, or
/// <see cref="Failed"/> with its error, which the runner has already reported. For the few UI Actions that must carry on
/// after a failed Operation; everything else uses <see cref="UiActions.Run{T}"/>, whose failure stops the action.
/// </summary>
/// <typeparam name="T">The work's result.</typeparam>
/// <remarks>
/// Only the runner creates outcomes, so a <see cref="Failed"/> always holds an error that is already in the Error Window
/// and the log.
/// </remarks>
public abstract class OperationOutcome<T>
{
    private OperationOutcome()
    {
    }

    /// <summary>
    /// Ends the UI Action here if the Operation failed, without reporting its error a second time; does nothing if it
    /// succeeded. For a caller that carried on past the failure and has now reached the steps it must skip.
    /// </summary>
    /// <exception cref="OperationFailedException">
    /// The Operation failed. <see cref="UiActions.Start"/>, <see cref="UiActions.Blocking"/> and the global handlers
    /// know it was already reported, so letting it propagate shows nothing more.
    /// </exception>
    public void ThrowIfFailed()
    {
        if (this is Failed failed)
        {
            throw new OperationFailedException(failed.OperationName, failed.Error);
        }
    }

    /// <summary>The Operation finished and returned <see cref="Value"/>.</summary>
    public sealed class Succeeded : OperationOutcome<T>
    {
        internal Succeeded(T value) => Value = value;

        /// <summary>What the work returned.</summary>
        public T Value { get; }
    }

    /// <summary>The Operation died; <see cref="Error"/> has been reported under its name.</summary>
    public sealed class Failed : OperationOutcome<T>
    {
        internal Failed(string operationName, Exception error)
        {
            OperationName = operationName;
            Error = error;
        }

        /// <summary>What the work threw, as it threw it, already reported.</summary>
        public Exception Error { get; }

        /// <summary>The Operation's name, as given to the runner; <see cref="ThrowIfFailed"/> carries it on.</summary>
        internal string OperationName { get; }
    }
}
