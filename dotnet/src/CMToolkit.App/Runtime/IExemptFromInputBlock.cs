namespace CMToolkit.App.Runtime;

/// <summary>
/// Marks a window that the <see cref="InputBlocker"/> must not attach when it opens over an attached window: one that
/// stays live during a blocking operation because code awaits its answer, such as the message box.
/// </summary>
public interface IExemptFromInputBlock;
