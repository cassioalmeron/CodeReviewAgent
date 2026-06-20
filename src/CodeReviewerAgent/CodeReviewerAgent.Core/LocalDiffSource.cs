namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The diff of the local working tree against the last commit (HEAD).
    /// </summary>
    public class LocalDiffSource : IDiffSource
    {
        public string GetDiff() => ProcessRunner.Run("git", "diff HEAD");
    }
}
