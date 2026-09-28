namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of the local repository. Prefers the staged changes when there are
/// any; otherwise falls back to the working tree against the last commit (HEAD).
/// </summary>
/// <param name="repositoryDirectory">Where to run git; blank means the current directory.</param>
public class LocalDiffSource(string repositoryDirectory = "") : IDiffSource
{
    private readonly StagedDiffSource _staged = new(repositoryDirectory);

    public string GetDiff()
    {
        var staged = _staged.GetDiff();
        return string.IsNullOrWhiteSpace(staged)
            ? ProcessRunner.Run("git", repositoryDirectory, "diff HEAD")
            : staged;
    }
}
