namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of one or more specific files against the last commit (HEAD).
/// </summary>
public class FilesDiffSource : IDiffSource
{
    private readonly IReadOnlyList<string> _paths;

    public FilesDiffSource(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one file path is required.", nameof(paths));
        _paths = paths;
    }

    public string GetDiff() => ProcessRunner.Run("git", ["diff", "HEAD", "--", .. _paths]);
}
