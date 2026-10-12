using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Problems;

/// <summary>
/// One problem the Scanner lists (the Core seams resolution on issue #7): the single immutable record that replaces the
/// reference's split between <c>helpers.ProblemInfo</c> (a file problem, with paths) and <c>SimpleProblemInfo</c> (a
/// plain text item). The Overview's problems (OVW-P1..) are the first ones built.
/// </summary>
/// <param name="Kind">The result tree group: a <c>ProblemType</c> value, or a <c>SimpleProblemInfo</c>'s problem text.</param>
/// <param name="ItemText">The tree item's text: the file name (<c>path.name</c>), or the simple problem's text.</param>
/// <param name="OpenablePath">
/// The path the details pane can open (SCN-R5), or null for a simple problem, whose path is a plain string.
/// </param>
/// <param name="RelativePath">The details pane's file path text, <c>str(relative_path)</c>.</param>
/// <param name="Mod">
/// The mod column: <c>&lt;Unmanaged&gt;</c> for a file problem with no mod, except <c>File Not Found</c> ones, which get
/// an empty string, as do all simple problems.
/// </param>
/// <param name="Summary">The problem text, verbatim from the reference.</param>
/// <param name="Solution">The solution text (a <c>SolutionType</c> value or authored text), or null for none.</param>
public sealed record Problem(
    string Kind,
    string ItemText,
    PythonPath? OpenablePath,
    string RelativePath,
    string Mod,
    string Summary,
    string? Solution)
{
    /// <summary>The links or lines shown after the solution, such as a Nexus URL; null for none.</summary>
    public IReadOnlyList<string>? ExtraData { get; init; }

    /// <summary>
    /// <c>ProblemInfo(problem, path, relative_path, mod, summary, solution)</c>: a problem about a file.
    /// </summary>
    /// <param name="kind">The <c>ProblemType</c> value, such as <see cref="ProblemTypes.FileNotFound"/>.</param>
    /// <param name="path">The file; its name is the tree item's text.</param>
    /// <param name="relativePath">The path shown in the details pane.</param>
    /// <param name="mod">The mod it belongs to, or null.</param>
    /// <param name="summary">The problem text.</param>
    /// <param name="solution">The solution text, or null.</param>
    /// <param name="extraData">The lines after the solution, or null.</param>
    public static Problem ForFile(
        string kind,
        PythonPath path,
        PythonPath relativePath,
        string? mod,
        string summary,
        string? solution,
        IReadOnlyList<string>? extraData = null)
        // ProblemInfo.__init__: mod or ("<Unmanaged>" if problem != ProblemType.FileNotFound else "").
        => new(kind, path.Name, path, relativePath.ToString(), mod is { Length: > 0 } ? mod : kind == ProblemTypes.FileNotFound ? string.Empty : "<Unmanaged>", summary, solution)
        {
            ExtraData = extraData,
        };

    /// <summary>
    /// <c>SimpleProblemInfo(path, problem, summary, solution)</c>: a problem about something that isn't one file, whose
    /// text stands in the tree and the details pane alike. Its mod is always empty.
    /// </summary>
    /// <param name="path">The item text, such as <c>Fallout4.exe</c> or <c>254 Full Modules</c>.</param>
    /// <param name="problem">The group, such as <c>Limit Exceeded</c>.</param>
    /// <param name="summary">The problem text.</param>
    /// <param name="solution">The solution text.</param>
    /// <param name="extraData">The lines after the solution, or null.</param>
    public static Problem Simple(string path, string problem, string summary, string solution, IReadOnlyList<string>? extraData = null)
        => new(problem, path, null, path, string.Empty, summary, solution) { ExtraData = extraData };
}

/// <summary>The <c>enums.ProblemType</c> values: the result tree groups of file problems.</summary>
public static class ProblemTypes
{
    /// <summary><c>ProblemType.FileNotFound</c>.</summary>
    public const string FileNotFound = "File Not Found";

    /// <summary><c>ProblemType.WrongVersion</c>.</summary>
    public const string WrongVersion = "Wrong Version";
}

/// <summary>The <c>enums.SolutionType</c> texts the problems use.</summary>
public static class SolutionTypes
{
    /// <summary><c>SolutionType.DownloadMod</c>, followed by the mod's link in the extra data.</summary>
    public const string DownloadMod = "Download the mod here:";

    /// <summary><c>SolutionType.VerifyFiles</c>.</summary>
    public const string VerifyFiles =
        "Verify files with Steam or reinstall the game.\nIf you downgraded the game you will need to do so again afterward.";
}
