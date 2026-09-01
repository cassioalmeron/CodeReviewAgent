namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The diff of the changes staged in the index (`git diff --staged`).
    /// </summary>
    public class StagedDiffSource : IDiffSource
    {
        public string GetDiff() => ProcessRunner.Run("git", "diff --staged");
    }
}
