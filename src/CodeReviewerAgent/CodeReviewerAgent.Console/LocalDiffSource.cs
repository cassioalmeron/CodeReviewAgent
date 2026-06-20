namespace CodeReviewerAgent.Console
{
    /// <summary>
    /// The diff of the local working tree against the last commit (HEAD).
    /// </summary>
    internal class LocalDiffSource : IDiffSource
    {
        public string GetDiff() => ProcessRunner.Run("git", "diff HEAD");
    }
}
