namespace CMToolkit.Core;

/// <summary>
/// The machine the app runs on, as far as Core needs to know it: the facts Core reads from the OS rather than from
/// files it is pointed at. Parity tests swap in a fake driven by a Parity Scenario's <c>host</c> block, which also
/// drives the Python driver's monkeypatches, so both sides see the same fake machine (ADR-0004).
/// </summary>
/// <remarks>
/// Deliberately small: each slice adds the members it needs, such as the registry, known folders, the parent-process
/// chain, file-version resources and the OS build (the Core seams resolution on issue #7). There is no file-system
/// member; parity fixtures are real temp trees.
/// </remarks>
public interface IHostEnvironment
{
    /// <summary>
    /// The folder holding the exe, where <c>assets\</c> lives. The Reference Implementation's equivalent is
    /// <c>sys._MEIPASS</c> (<c>utils.get_asset_path</c>).
    /// </summary>
    string AppDirectory { get; }
}

/// <summary>The real machine.</summary>
public sealed class SystemHostEnvironment : IHostEnvironment
{
    private SystemHostEnvironment()
    {
    }

    /// <summary>The single instance; the machine has no state of its own to hold.</summary>
    public static SystemHostEnvironment Instance { get; } = new();

    /// <inheritdoc/>
    public string AppDirectory => AppContext.BaseDirectory;
}
