using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.EnvironmentDetection;

/// <summary>
/// The installation the toolkit works on, built once at startup and never changed during a session (the Core seams
/// resolution on issue #7): what <c>GameInfo.__init__</c> leaves behind, without the per-refresh Overview state.
/// </summary>
/// <param name="Manager">The mod manager that launched the toolkit, or null (MM-1).</param>
/// <param name="Mo2">MO2's configuration, or null without MO2 (MO2-9, MO2-11).</param>
/// <param name="Directories">The game folder and its Data and F4SE Plugins folders (GP-1..GP-4).</param>
/// <param name="Inis">The game INIs and the language (INI-2..INI-4).</param>
public sealed record GameInstall(ModManager? Manager, Mo2Configuration? Mo2, GameDirectories Directories, GameInis Inis)
{
    /// <summary>The game folder (<c>game_path</c>).</summary>
    public PythonPath GamePath => Directories.GamePath;

    /// <summary>The Data folder, or null if it isn't one (<c>data_path</c>).</summary>
    public PythonPath? DataPath => Directories.DataPath;

    /// <summary>The F4SE Plugins folder, or null if it isn't one (<c>f4se_path</c>).</summary>
    public PythonPath? F4sePath => Directories.F4sePath;
}
