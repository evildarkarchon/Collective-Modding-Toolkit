namespace CMToolkit.Core.PythonSemantics;

/// <summary>
/// CPython's <c>os.chmod</c> on Windows, which only ever touches the read-only attribute: it reads the file's
/// attributes, clears or sets <c>FILE_ATTRIBUTE_READONLY</c>, and writes the word back through the file's handle
/// (<c>SetFileInformationByHandle</c>), where an attribute word of 0 means "leave the attributes as they are".
/// </summary>
internal static class PythonChmod
{
    /// <summary>
    /// <c>path.chmod(stat.S_IWRITE)</c>: clears the read-only attribute, keeping the others. When read-only is the
    /// file's only attribute, CPython wrote back a word of 0, which changes nothing, so the file stayed read-only (bug
    /// #91; verified against CPython 3.14.5). The port fixes that as a Deviation (ADR-0006): it writes
    /// <c>FILE_ATTRIBUTE_NORMAL</c> then, so the file is always writable afterwards.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <exception cref="IOException">Windows refused the change (Python's <c>OSError</c>).</exception>
    /// <exception cref="UnauthorizedAccessException">The attributes can't be written (also Python's <c>OSError</c>).</exception>
    public static void SetWritable(string path)
    {
        var remaining = File.GetAttributes(path) & ~FileAttributes.ReadOnly;
        // Bug #91: an empty word is ignored by Windows, so say NORMAL ("no other attributes") instead.
        File.SetAttributes(path, remaining == 0 ? FileAttributes.Normal : remaining);
    }
}
