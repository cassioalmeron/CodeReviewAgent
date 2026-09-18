namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// Turns a scored golden run into the <see cref="GoldenRun"/> that is stored: its totals, its latency
/// percentiles, one score per case and the five gates. Pure, so an import of recorded rounds and a
/// live run produce the same row from the same rounds.
/// </summary>
public static class GoldenRunSummary
{
    /// <param name="identity">
    /// The run as far as the caller knows it: model, engine, skills, prompt version, start, duration
    /// and report. Everything else is computed here and replaces whatever it carried.
    /// </param>
    public static GoldenRun Build(
        GoldenRun identity, IReadOnlyList<GoldenCaseResult> results, IReadOnlyList<ReviewResult> reviews)
    {
        var latencies = reviews.Select(r => r.LatencyMs).Order().ToList();
        var verdict = GoldenGates.Judge(results);

        return identity with
        {
            Cost = reviews.Sum(r => r.Cost),
            InputTokens = reviews.Sum(r => r.InputTokens),
            OutputTokens = reviews.Sum(r => r.OutputTokens),
            LatencyP50Ms = Percentile(latencies, 50),
            LatencyP95Ms = Percentile(latencies, 95),
            LatencyP99Ms = Percentile(latencies, 99),
            Approved = verdict.Approved,
            Cases = [.. results.Select(ToScore)],
            Gates = [.. verdict.Gates.Select(ToGate)],
        };
    }

    /// <summary>
    /// Nearest rank: the value at position ceil(p% of n), counted from 1, in integer arithmetic. With
    /// 75 reviews the P99 is simply the slowest one, which is stated rather than interpolated away.
    /// </summary>
    public static long Percentile(IReadOnlyList<long> sortedAscending, int percent)
    {
        if (sortedAscending.Count == 0)
            return 0;

        var rank = (percent * sortedAscending.Count + 99) / 100;
        return sortedAscending[Math.Max(rank, 1) - 1];
    }

    private static GoldenCaseScore ToScore(GoldenCaseResult result) => new()
    {
        CaseName = result.Name,
        Kind = result.Kind,
        Runs = result.Runs,
        Successes = result.Successes,
        CleanRounds = result.CleanRounds,
        PrecisionCorrect = result.PrecisionCorrect,
        PrecisionCounted = result.PrecisionCounted,
        ExactCalibrations = (result.CalibrationDistances ?? []).Count(distance => distance == 0),
        DiscardedFindings = result.DiscardedFindings,
        Approved = GoldenGates.CaseApproved(result),
    };

    private static GoldenGate ToGate(Gate gate) => new()
    {
        Name = gate.Name,
        Part = gate.Part,
        Whole = gate.Whole,
        Floor = gate.Floor,
        Direction = gate.Direction,
        Passed = gate.Passed,
    };
}
