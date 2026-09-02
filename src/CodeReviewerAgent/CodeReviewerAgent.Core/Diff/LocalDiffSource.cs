namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of the local repository. Prefers the staged changes when there are
/// any; otherwise falls back to the working tree against the last commit (HEAD).
/// </summary>
public class LocalDiffSource : IDiffSource
{
    private readonly StagedDiffSource _staged = new();

    public string GetDiff()
    {
        var staged = _staged.GetDiff();
        return string.IsNullOrWhiteSpace(staged)
            ? ProcessRunner.Run("git", "diff HEAD")
            : staged;
    }
}
