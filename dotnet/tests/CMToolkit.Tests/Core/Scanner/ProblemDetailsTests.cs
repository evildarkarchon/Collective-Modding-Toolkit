using CMToolkit.Core.Problems;
using CMToolkit.Core.PythonSemantics;
using CMToolkit.Core.Scanner;

namespace CMToolkit.Tests.Core.Scanner;

/// <summary>
/// The details pane's texts for the cases no recorded scenario reaches, because no reference problem has them today:
/// extra lines that aren't a link, an empty extra list, an empty solution, and a File List that isn't the race subgraph
/// count's (<c>ResultDetailsPane.set_info</c>, <c>copy_details</c>). The recorded cases are in the
/// <c>scanner-details</c> scenarios.
/// </summary>
public sealed class ProblemDetailsTests
{
    private static readonly IReadOnlyList<(int Count, PythonPath File)> Files = [(3, new PythonPath(@"C:\Data\a.esp"))];

    [Fact]
    [Trait("Parity", "SCN-R6")]
    public void Extra_lines_follow_the_solution_but_only_a_leading_http_line_is_a_link()
    {
        var problem = Problem.Simple("x", "Kind", "Summary", "Do this:", extraData: ["see the readme", "https://example.com"]);

        Assert.Equal("Do this:\nsee the readme\nhttps://example.com", ProblemDetails.SolutionText(problem));
        Assert.Null(ProblemDetails.Url(problem));
    }

    [Fact]
    [Trait("Parity", "SCN-R6")]
    public void An_empty_extra_list_adds_nothing_and_an_empty_solution_reads_as_none()
    {
        // `extra_data` and `solution` are tested for truth, so [] and "" count as missing.
        var problem = Problem.Simple("x", "Kind", "Summary", "", extraData: []);

        Assert.Equal(ProblemDetails.NoSolution, ProblemDetails.SolutionText(problem));
        Assert.Null(ProblemDetails.Url(problem));
    }

    [Fact]
    [Trait("Parity", "SCN-R7")]
    public void A_file_list_that_isnt_the_race_subgraph_counts_opens_as_Files_with_no_text()
    {
        var problem = Problem.Simple("x", "Other Kind", "Summary", "Solution") with { FileList = Files };

        Assert.Equal(("Files", ""), ProblemDetails.FileListWindow(problem));
    }

    [Fact]
    [Trait("Parity", "SCN-R7")]
    public void Only_a_simple_problem_with_a_non_empty_file_list_has_a_File_List()
    {
        var file = Problem.ForFile(ProblemTypes.WrongVersion, new PythonPath(@"C:\Game\a.exe"), new PythonPath("a.exe"), null, "Summary", null)
            with { FileList = Files };
        var empty = Problem.Simple("x", ProblemDetails.RaceSubgraphKind, "Summary", "Solution") with { FileList = [] };

        Assert.Null(ProblemDetails.FileListWindow(file));
        Assert.Null(ProblemDetails.FileListWindow(empty));
    }

    [Fact]
    [Trait("Parity", "SCN-R7")]
    public void Copy_Details_copies_the_mod_line_only_in_stage_mode()
    {
        var problem = Problem.Simple("x.esp", "Kind", "Summary", "Solution");

        Assert.Equal("Problem: x.esp\nSummary: Summary\nSolution: Solution\n", ProblemDetails.CopyText(problem, usingStage: false));
        Assert.Equal("Mod: N/A\nProblem: x.esp\nSummary: Summary\nSolution: Solution\n", ProblemDetails.CopyText(problem, usingStage: true));
    }
}
