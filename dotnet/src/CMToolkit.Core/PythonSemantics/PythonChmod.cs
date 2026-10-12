namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// CPython's <c>os.chmod</c> on Windows, which only ever touches the read-only attribute: it reads the file's
/// attributes, clears or sets <c>FILE_ATTRIBUTE_READONLY</c>, and writes the word back through the file's handle
/// (<c>SetFileInformationByHandle</c>), where an attribute word of 0 means "leave the attributes as they are".
/// </summary>
internal static class PythonChmod
{
    /// <summary>
    /// <c>path.chmod(stat.S_IWRITE)</c>. When read-only is the file's only attribute, clearing it leaves a word of 0,
    /// which changes nothing, so the file stays read-only (bug #91, reproduced, not fixed yet; verified against CPython
    /// 3.14.5). <see cref="File.SetAttributes(string, FileAttributes)"/> would clear it, so that case is left alone.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">Windows refused the change (Python's <c>OSError</c>).</exception>
    /// <exception cref="UnauthorizedAccessException">The attributes can't be written (also Python's <c>OSError</c>).</exception>
    public static void SetWritable(string path)
    {
        var remaining = File.GetAttributes(path) & ~FileAttributes.ReadOnly;
        if (remaining == 0)
        {
            // Bug #91: the zero word CPython writes is ignored by Windows.
            return;
        }

        File.SetAttributes(path, remaining);
    }
}
