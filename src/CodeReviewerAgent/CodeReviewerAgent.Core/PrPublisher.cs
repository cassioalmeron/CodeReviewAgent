namespace CodeReviewerAgent.Core;

/// <summary>
/// Publishes a review as a comment on a pull request via the GitHub CLI (`gh pr comment`).
/// Requires `gh` to be authenticated with write access to the repository.
/// </summary>
public static class PrPublisher
{
    public static void Publish(int prNumber, ReviewResult review)
    {
        var body = PrCommentFormatter.Format(review);

        // Pass the body through a file so long comments never hit the command-line length limit.
        var tempFile = Path.Combine(Path.GetTempPath(), $"pr-comment-{Guid.NewGuid():N}.md");
        File.WriteAllText(tempFile, body);
        try
        {
            ProcessRunner.Run("gh", "pr", "comment", prNumber.ToString(), "--body-file", tempFile);
        }
        finally
        {
            File.Delete(tempFile);
        }

        System.Console.WriteLine($"Published review to PR #{prNumber}.");
    }
}
