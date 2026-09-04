using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The whole point of <c>EVAL_OUTPUT_DIR</c>: months of paid evaluation history stop living in
/// the build output, where a clean rebuild erases them. Nothing else in the suite pins that
/// down, and a silent regression would send the archive back to <c>bin</c> without a test going
/// red.
/// <para>
/// These exercise <see cref="OutputPaths.Resolve"/> rather than the properties. The properties
/// answer from a value resolved once per process, which is right for the application (the
/// environment is loaded at startup and never changes) and useless to assert against: whichever
/// test touched the class first would decide the answer for all the others.
/// </para>
/// </summary>
public class OutputPathsTests
{
    [Fact]
    public void Resolve_UsesTheConfiguredDirectory()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cra-output-probe");

        Assert.Equal(configured, WithVariable(configured, OutputPaths.Resolve));
    }

    /// <summary>
    /// Unset falls back to the build output, which is what someone who clones the repository and
    /// runs it with no configuration gets.
    /// </summary>
    [Fact]
    public void Resolve_FallsBackToTheBuildOutputWhenUnset() =>
        Assert.Equal(AppContext.BaseDirectory, WithVariable(null, OutputPaths.Resolve));

    /// <summary>Blank is not a directory: whitespace reads as unset, never as the drive root.</summary>
    [Fact]
    public void Resolve_ReadsBlankAsUnset() =>
        Assert.Equal(AppContext.BaseDirectory, WithVariable("   ", OutputPaths.Resolve));

    /// <summary>Trailing whitespace in the file is the author's, not part of the path.</summary>
    [Fact]
    public void Resolve_TrimsWhatItReads()
    {
        var configured = Path.Combine(Path.GetTempPath(), "cra-output-probe");

        Assert.Equal(configured, WithVariable($"  {configured}  ", OutputPaths.Resolve));
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

    private static string WithVariable(string? value, Func<string> read)
    {
        var previous = Environment.GetEnvironmentVariable("EVAL_OUTPUT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("EVAL_OUTPUT_DIR", value);
            return read();
        }
        finally { Environment.SetEnvironmentVariable("EVAL_OUTPUT_DIR", previous); }
    }
}
