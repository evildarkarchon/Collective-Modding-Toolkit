namespace CMToolkit.Tests.Support;

/// <summary>
/// A uniquely named directory under the system temp folder, deleted with its contents on dispose. Each test gets its
/// own, so tests that touch the file system can run in parallel.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cmt-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Absolute path of the directory.</summary>
    public string Path { get; }

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="relativePath"/>, creating parent folders.</summary>
    /// <returns>The file's absolute path.</returns>
    public string WriteBytes(string relativePath, byte[] bytes)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail the test that used it.
        }
    }
}
