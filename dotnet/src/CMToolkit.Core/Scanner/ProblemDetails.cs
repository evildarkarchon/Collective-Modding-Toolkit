using CMToolkit.Core.EnvironmentDetection;
using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Scanner;

/// <summary>
/// What the Scanner's details pane shows for a result (<c>ResultDetailsPane.set_info</c> and <c>copy_details</c>):
/// the row texts (SCN-R4), the folder its path opens (SCN-R5), the solution text and its link (SCN-R6), the Copy Details
/// text and the File List window (SCN-R7).
/// </summary>
public static class ProblemDetails
{
    /// <summary>The solution text of a result that has none.</summary>
    public const string NoSolution = "No solution suggestion.";

    /// <summary>The mod row's text for a result without a mod.</summary>
    public const string NoMod = "N/A";

    /// <summary><c>TOOLTIP_LOCATION</c>: the path's tooltip when it can be opened.</summary>
    public const string LocationTooltip = "Click to open location";

    /// <summary>The solution's tooltip when its first extra line is a link.</summary>
    public const string UrlTooltip = "Left-Click: Open URL\nRight-Click: Copy URL";

    /// <summary>The File List window's column headings, the second with its leading space.</summary>
    public static readonly IReadOnlyList<string> FileListHeaders = ["Records", " Module"];

    /// <summary>The File List window's size: <c>TreeWindow(..., 400, 500, ...)</c>.</summary>
    public const int FileListWidth = 400;

    /// <inheritdoc cref="FileListWidth"/>
    public const int FileListHeight = 500;

    /// <summary>The Mod row's value: <c>mod or 'N/A'</c>.</summary>
    public static string ModText(Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.Mod is { Length: > 0 } mod ? mod : NoMod;
    }

    /// <summary>
    /// The Solution row's value: <c>solution or 'No solution suggestion.'</c>, then a newline and the extra lines when
    /// there are any.
    /// </summary>
    public static string SolutionText(Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var solution = problem.Solution is { Length: > 0 } text ? text : NoSolution;
        // `if self.problem_info.extra_data:` — an empty list is falsy, so it adds nothing, not a lone newline.
        return problem.ExtraData is { Count: > 0 } extra ? solution + "\n" + string.Join("\n", extra) : solution;
    }

    /// <summary>
    /// The solution's link: the first extra line when it starts with <c>http</c> (left-click opens it, right-click
    /// copies it), or null when the solution isn't clickable.
    /// </summary>
    public static string? Url(Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return problem.ExtraData is { Count: > 0 } extra && extra[0].StartsWith("http", StringComparison.Ordinal) ? extra[0] : null;
    }

    /// <summary>
    /// The folder a click on the path opens in Explorer (SCN-R5), checked when the result is shown: the path itself when
    /// it is a folder, else its parent, provided the path or its parent exists. Null when the path can't be opened: a
    /// simple problem's path is plain text, or neither it nor its parent exists.
    /// </summary>
    /// <param name="host">The machine: a relative path is relative to its working directory, as the reference's was.</param>
    /// <param name="problem">The result shown.</param>
    /// <returns>The folder as the reference passed it to <c>os.startfile</c> (relative if the path was).</returns>
    public static PythonPath? Location(IHostEnvironment host, Problem problem)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(problem);
        if (problem.OpenablePath is not { } target)
        {
            return null;
        }

        if (!HostPaths.Exists(host, target) && !HostPaths.Exists(host, target.Parent))
        {
            return null;
        }

        // A missing path whose parent exists isn't a folder either, so its parent is opened.
        return HostPaths.IsDirectory(host, target) ? target : target.Parent;
    }

    /// <summary>
    /// <c>copy_details</c>: what Copy Details puts on the clipboard, the shown rows as <c>Label: value</c> lines, each
    /// ending in a newline.
    /// </summary>
    /// <param name="problem">The result shown.</param>
    /// <param name="usingStage">Whether the Mod row is shown (stage mode), and so copied.</param>
    public static string CopyText(Problem problem, bool usingStage)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var mod = usingStage ? $"Mod: {ModText(problem)}\n" : "";
        return $"{mod}Problem: {problem.RelativePath}\nSummary: {problem.Summary}\nSolution: {SolutionText(problem)}\n";
    }

    /// <summary>
    /// The File List window a result offers (SCN-R7): only a simple problem (a plain-text path) with a non-empty file
    /// list has one. The race subgraph count's window is titled <c>Race Animation Subgraph Records</c> over its info text
    /// re-flowed (newlines to spaces, then the first sentence break back to a newline); any other is <c>Files</c> with no
    /// text.
    /// </summary>
    /// <returns>The window's title and text, or null when the result has no File List button.</returns>
    public static (string Title, string Text)? FileListWindow(Problem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (problem.OpenablePath is not null || problem.FileList is not { Count: > 0 })
        {
            return null;
        }

        // The race subgraph count's group (SimpleProblemInfo.problem) is the one whose File List has its own title.
        if (problem.Kind != ScanProblemTypes.RaceSubgraphRecordCount)
        {
            return ("Files", "");
        }

        // INFO_SCAN_RACE_SUBGRAPHS.replace("\n", " ").replace(". ", ".\n", 1)
        var text = ScanSettingTexts.RaceSubgraphsInfo.Replace('\n', ' ');
        var at = text.IndexOf(". ", StringComparison.Ordinal);
        return ("Race Animation Subgraph Records", at < 0 ? text : string.Concat(text.AsSpan(0, at), ".\n", text.AsSpan(at + 2)));
    }
}
