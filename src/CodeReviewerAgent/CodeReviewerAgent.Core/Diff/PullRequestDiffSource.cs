namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of a real pull request, fetched via the GitHub CLI (`gh pr diff`).
/// </summary>
/// <param name="repositoryDirectory">Where to run the GitHub CLI; blank means the current directory.</param>
public class PullRequestDiffSource(int prNumber, string repositoryDirectory = "") : IDiffSource
{
    public string GetDiff() => ProcessRunner.Run("gh", repositoryDirectory, $"pr diff {prNumber}");
}