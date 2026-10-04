namespace CMToolkit.Tests.Support;

/// <summary>Source-tree locations, found by walking up from the test output folder to <c>CMToolkit.slnx</c>.</summary>
internal static class RepoPaths
{
    /// <summary>The <c>dotnet/</c> folder that holds <c>CMToolkit.slnx</c>.</summary>
    public static string DotnetRoot { get; } = FindDotnetRoot();

    /// <summary>The App project's folder.</summary>
    public static string AppProject => Path.Combine(DotnetRoot, "src", "CMToolkit.App");

    private static string FindDotnetRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CMToolkit.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException($"No CMToolkit.slnx above {AppContext.BaseDirectory}.");
    }
}
