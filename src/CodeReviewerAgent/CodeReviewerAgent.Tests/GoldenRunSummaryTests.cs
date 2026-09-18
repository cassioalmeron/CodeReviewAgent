using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class GoldenRunSummaryTests
{
    [Theory]
    [InlineData(50, 38)]  // ceil(37.5)
    [InlineData(95, 72)]  // ceil(71.25)
    [InlineData(99, 75)]  // ceil(74.25): with 75 reviews, the slowest one
    public void Percentile_IsNearestRank(int percent, long expected)
    {
        var latencies = Enumerable.Range(1, 75).Select(i => (long)i).ToList();

        Assert.Equal(expected, GoldenRunSummary.Percentile(latencies, percent));
    }

    [Fact]
    public void Percentile_OfNothing_IsZero() =>
        Assert.Equal(0, GoldenRunSummary.Percentile([], 50));

    [Fact]
    public void Build_SumsTheReviews_AndCarriesCasesAndRawGates()
    {
        GoldenCaseResult[] results =
        [
            new("sql-injection", GoldenKind.Detection, null, "v3", 5, 5, null,
                PrecisionCorrect: 5, PrecisionCounted: 5, CalibrationDistances: [0, 0, 0, 1, 0], CleanRounds: 4),
            new("safe-interpolation", GoldenKind.Trap, null, "v3", 4, 5, null,
                PrecisionCounted: 2, CleanRounds: 3, DiscardedFindings: 1),
        ];
        ReviewResult[] reviews =
        [
            new(null, [], Cost: 0.00006774m, LatencyMs: 300, InputTokens: 10, OutputTokens: 20),
            new(null, [], Cost: 0.00001000m, LatencyMs: 100, InputTokens: 5, OutputTokens: 1),
        ];

        var run = GoldenRunSummary.Build(new GoldenRun { Model = "m", DurationMs = 42 }, results, reviews);

        Assert.Equal("m", run.Model);
        Assert.Equal(42, run.DurationMs);
        Assert.Equal(0.00007774m, run.Cost);
        Assert.Equal((15, 21), (run.InputTokens, run.OutputTokens));
        Assert.Equal((100, 300, 300), (run.LatencyP50Ms, run.LatencyP95Ms, run.LatencyP99Ms));

        var detection = run.Cases!.Single(c => c.CaseName == "sql-injection");
        Assert.Equal(4, detection.ExactCalibrations);
        Assert.True(detection.Approved);
        Assert.False(run.Cases!.Single(c => c.CaseName == "safe-interpolation").Approved);

        var trapNoise = run.Gates!.Single(g => g.Name == "Trap noise");
        Assert.Equal((2, 5, 1, GateDirection.Max, true), (trapNoise.Part, trapNoise.Whole, trapNoise.Floor, trapNoise.Direction, trapNoise.Passed));
        Assert.Equal(5, run.Gates!.Count);
    }
}
