using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

internal static class GoldenRunResultExtensions
{
    /// <summary>
    /// The scored run, for a test that expects the set to finish. It asserts rather than using
    /// <c>!</c> so that a run which died reports the reason it died, instead of surfacing as a
    /// null reference somewhere further down the test.
    /// </summary>
    public static GoldenScore Scored(this GoldenRunResult result)
    {
        Assert.True(result.Succeeded, $"the run did not finish: {result.Failure}");
        Assert.NotNull(result.Score);
        return result.Score;
    }
}
