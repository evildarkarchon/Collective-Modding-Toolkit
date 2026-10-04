namespace CMToolkit.Tests.Support;

/// <summary>Keeps a test that awaits UI work from hanging the run when the work never completes.</summary>
public static class TaskTimeouts
{
    /// <summary>The longest any awaited UI step may take before the test fails.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    /// <summary>Awaits <paramref name="task"/>, failing with a <see cref="TimeoutException"/> after <see cref="Default"/>.</summary>
    public static Task WithTimeout(this Task task) => task.WaitAsync(Default);

    /// <inheritdoc cref="WithTimeout(Task)"/>
    public static Task<T> WithTimeout<T>(this Task<T> task) => task.WaitAsync(Default);
}
