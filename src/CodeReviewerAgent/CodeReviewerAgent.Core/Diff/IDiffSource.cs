namespace CodeReviewerAgent.Core.Diff;

/// <summary>
/// Provides the diff to be reviewed. Implementations decide where it comes from
/// (local working tree, a pull request, etc.).
/// </summary>
public interface IDiffSource
{
    string GetDiff();
}
