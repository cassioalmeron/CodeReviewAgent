namespace CodeReviewerAgent.Console
{
    /// <summary>
    /// Provides the diff to be reviewed. Implementations decide where it comes from
    /// (local working tree, a pull request, etc.).
    /// </summary>
    internal interface IDiffSource
    {
        string GetDiff();
    }
}
