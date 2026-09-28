using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The whole point of the configured output root: months of paid evaluation history stop living
/// in the build output, where a clean rebuild erases them. Nothing else in the suite pins that
/// down, and a silent regression would send the archive back to <c>bin</c> without a test going
/// red.
/// <para>
/// These exercise <see cref="OutputPaths.Resolve"/>, the rule, rather than the properties, which
/// answer from a value the entry point sets once at startup. The rule takes the value as an
/// argument now, so nothing here moves the process environment and the class no longer has to
/// serialize against the rest of the suite (US-018).
/// </para>
/// </summary>
public class OutputPathsTests
{
    [Fact]
    public void Resolve_UsesTheConfiguredDirectory()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cra-output-probe");

        Assert.Equal(configured, OutputPaths.Resolve(configured));
    }

    /// <summary>
    /// Unset falls back to the build output, which is what someone who clones the repository and
    /// runs it with no configuration gets.
    /// </summary>
    [Fact]
    public void Resolve_FallsBackToTheBuildOutputWhenUnset() =>
        Assert.Equal(AppContext.BaseDirectory, OutputPaths.Resolve(null));

    /// <summary>Blank is not a directory: whitespace reads as unset, never as the drive root.</summary>
    [Fact]
    public void Resolve_ReadsBlankAsUnset() =>
        Assert.Equal(AppContext.BaseDirectory, OutputPaths.Resolve("   "));

    /// <summary>Trailing whitespace in the configuration file is the author's, not part of the path.</summary>
    [Fact]
    public void Resolve_TrimsWhatItReads()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cra-output-probe");

        Assert.Equal(configured, OutputPaths.Resolve($"  {configured}  "));
    }

    /// <summary>
    /// The two folders hang off the resolved root, whatever it resolved to. Asserting the
    /// relationship rather than the value is what makes this independent of test order.
    /// </summary>
    [Fact]
    public void ReportsAndReviews_HangOffTheRoot()
    {
        Assert.Equal(Path.Combine(OutputPaths.Root, "reports"), OutputPaths.Reports);
        Assert.Equal(Path.Combine(OutputPaths.Root, "reviews"), OutputPaths.Reviews);
    }
}
