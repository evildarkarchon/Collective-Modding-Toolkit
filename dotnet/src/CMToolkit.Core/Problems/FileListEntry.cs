using CMToolkit.Core.PythonSemantics;

namespace CMToolkit.Core.Problems;

/// <summary>One <c>file_list</c> entry of a <see cref="Problem"/>: <c>(count, path)</c>.</summary>
/// <param name="Count">The count, such as a module's SADD records (the TreeWindow's <c>Records</c> column).</param>
/// <param name="File">The file it was counted in (the <c> Module</c> column).</param>
public sealed record FileListEntry(int Count, PythonPath File);
