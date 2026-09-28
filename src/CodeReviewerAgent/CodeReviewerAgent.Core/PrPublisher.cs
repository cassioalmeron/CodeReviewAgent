namespace CodeReviewerAgent.Core;

/// <summary>
/// Publishes a review as a comment on a pull request via the GitHub CLI (`gh pr comment`).
/// Requires `gh` to be authenticated with write access to the repository.
/// </summary>
public static class PrPublisher
{
    /// <param name="repositoryDirectory">Where to run the GitHub CLI; blank means the current directory.</param>
    public static void Publish(int prNumber, ReviewResult review, string repositoryDirectory = "")
    {
        var body = PrCommentFormatter.Format(review);

        // Pass the body through a file so long comments never hit the command-line length limit.
        var tempFile = Path.Combine(Path.GetTempPath(), $"pr-comment-{Guid.NewGuid():N}.md");
        File.WriteAllText(tempFile, body);
        try
        {
            ProcessRunner.Run(
                "gh", repositoryDirectory, "pr", "comment", prNumber.ToString(), "--body-file", tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
