namespace CodeReviewerAgent.Core.Diff;

public enum DiffChangeType
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

/// <summary>
/// A single line inside a hunk. <see cref="OldLineNumber"/> is null for added
/// lines and <see cref="NewLineNumber"/> is null for removed lines.
/// </summary>
public sealed record DiffLine(
    DiffLineKind Kind,
    int? OldLineNumber,
    int? NewLineNumber,
    string Text);

/// <summary>
/// A contiguous block of changes, as described by a <c>@@ -old +new @@</c> header.
/// </summary>
public sealed record Hunk(
    int OldStart,
    int OldCount,
    int NewStart,
    int NewCount,
    string HeaderContext,
    IReadOnlyList<DiffLine> Lines);

/// <summary>
/// The changes to a single file. <see cref="OldPath"/> is null for added files
/// and <see cref="NewPath"/> is null for deleted files.
/// </summary>
public sealed record DiffFile(
    string? OldPath,
    string? NewPath,
    DiffChangeType ChangeType,
    IReadOnlyList<Hunk> Hunks)
{
    /// <summary>The file's current path, falling back to the old one when deleted.</summary>
    public string? Path => NewPath ?? OldPath;
}

/// <summary>
/// A unified diff parsed into files, hunks, and lines with absolute line numbers.
/// </summary>
public sealed record ParsedDiff(IReadOnlyList<DiffFile> Files);
