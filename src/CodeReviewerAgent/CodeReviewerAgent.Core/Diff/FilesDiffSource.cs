namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of one or more specific files against the last commit (HEAD).
/// </summary>
/// <param name="repositoryDirectory">Where to run git; blank means the current directory.</param>
public class FilesDiffSource : IDiffSource
{
    private readonly IReadOnlyList<string> _paths;
    private readonly string _repositoryDirectory;

    public FilesDiffSource(IReadOnlyList<string> paths, string repositoryDirectory = "")
    {
        if (paths.Count == 0)
            throw new ArgumentException("At least one file path is required.", nameof(paths));
        _paths = paths;
        _repositoryDirectory = repositoryDirectory;
    }

    public string GetDiff() =>
        ProcessRunner.Run("git", _repositoryDirectory, ["diff", "HEAD", "--", .. _paths]);
}
