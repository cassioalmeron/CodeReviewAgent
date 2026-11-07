using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// Each eval set writes its own result file, so a run of one set cannot erase the last run of
/// another. The golden set keeps the bare name because the judge reads that file by name.
/// </summary>
public class GoldenResultsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"cra-results-{Guid.NewGuid():N}");

    private static ReviewResult Review(string summary) =>
        new(summary, [], "fake", "fake-model", "v3", 0m, 0, 0, 0, "diff");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void GoldenResultsFileTests_The_golden_set_keeps_the_name_the_judge_reads()
    {
        var path = GoldenEvaluatorReport.PersistReviews([Review("golden")], GoldenEvaluator.GoldenSet, _directory);

        Assert.Equal(Path.Combine(_directory, "eval-results.json"), path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void GoldenResultsFileTests_A_named_set_writes_its_own_file_and_leaves_the_golden_one_untouched()
    {
        var goldenPath = GoldenEvaluatorReport.PersistReviews([Review("golden")], GoldenEvaluator.GoldenSet, _directory);
        var goldenBefore = File.ReadAllText(goldenPath);

        var routingPath = GoldenEvaluatorReport.PersistReviews([], "routing", _directory);

        Assert.Equal(Path.Combine(_directory, "eval-results-routing.json"), routingPath);
        Assert.Equal("[]", File.ReadAllText(routingPath).Trim());
        Assert.Equal(goldenBefore, File.ReadAllText(goldenPath));
        Assert.Contains("golden", goldenBefore);
    }

    [Fact]
    public void GoldenResultsFileTests_The_file_name_the_judge_reads_is_the_one_the_golden_set_writes()
    {
        Assert.Equal("eval-results.json", GoldenEvaluatorReport.ResultsFileName(GoldenEvaluator.GoldenSet));
        Assert.Equal("eval-results-routing.json", GoldenEvaluatorReport.ResultsFileName("routing"));
    }
}
