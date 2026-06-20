namespace CodeReviewerAgent.Console
{
    /// <summary>
    /// The diff of a real pull request, fetched via the GitHub CLI (`gh pr diff`).
    /// </summary>
    internal class PullRequestDiffSource : IDiffSource
    {
        private readonly int _prNumber;

        public PullRequestDiffSource(int prNumber) => _prNumber = prNumber;

        public string GetDiff() => ProcessRunner.Run("gh", $"pr diff {_prNumber}");
    }
}