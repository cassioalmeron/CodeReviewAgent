namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// The diff of the changes staged in the index (`git diff --staged`).
/// </summary>
/// <param name="repositoryDirectory">Where to run git; blank means the current directory.</param>
public class StagedDiffSource(string repositoryDirectory = "") : IDiffSource
{
    public string GetDiff() => ProcessRunner.Run("git", repositoryDirectory, "diff --staged");
}
