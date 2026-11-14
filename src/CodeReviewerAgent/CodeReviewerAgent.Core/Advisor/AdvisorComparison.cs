using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core.Advisor;

/// <summary>
/// One case seen from the three recorded passes, round by round and in the same order: the cheap
/// review that was the first review, the strong model's review, and the advisor pass's final review.
/// </summary>
public sealed record AdvisorCaseRounds(
    GoldenCase Case,
    IReadOnlyList<ReviewResult> Cheap,
    IReadOnlyList<ReviewResult> Strong,
    IReadOnlyList<ReviewResult> Advisor);

/// <summary>One column of the comparison: the five gates, the total cost and the total latency.</summary>
public sealed record AdvisorColumn(
    string Label, IReadOnlyList<GoldenCaseResult> Results, ModelVerdict Verdict, decimal Cost, long LatencyMs);

/// <summary>A case's result under one column, with what that column cost on the case.</summary>
public sealed record AdvisorCaseColumn(GoldenCaseResult Result, decimal Cost);

public sealed record AdvisorCaseLine(
    string Name,
    GoldenKind Kind,
    int Escalated,
    AdvisorCaseColumn Cheap,
    AdvisorCaseColumn Strong,
    AdvisorCaseColumn Replace,
    AdvisorCaseColumn Merge);

/// <summary>
/// What the second review did to the escalated rounds of one kind of case, against the first review.
/// On a detection case a gain is a miss that became a catch; on a trap a gain is a round that fell
/// for the bait and now resists it. A loss is the reverse.
/// </summary>
public sealed record Transitions(int Gained, int Lost);

public sealed record AdvisorTransitions(Transitions Detection, Transitions Trap);

public sealed record AdvisorComparison(
    IReadOnlyList<AdvisorCaseLine> Cases,
    AdvisorColumn Cheap,
    AdvisorColumn Strong,
    AdvisorColumn Replace,
    AdvisorColumn Merge,
    int Rounds,
    int Escalated,
    int ZeroHints,
    int AdvisorFailed,
    decimal AdvisorCallsCost,
    AdvisorTransitions ReplaceTransitions,
    AdvisorTransitions MergeTransitions)
{
    /// <summary>
    /// Composes the four columns from the recorded passes, with no model call. The merge column keeps,
    /// for an escalated round, the first review's findings plus the final review's that the first did
    /// not already have; it costs what the replace column costs, since it is the same calls.
    /// </summary>
    public static AdvisorComparison Compose(IReadOnlyList<AdvisorCaseRounds> cases, string promptVersion)
    {
        var lines = new List<AdvisorCaseLine>();
        var rounds = 0;
        var escalated = 0;
        var zeroHints = 0;
        var advisorFailed = 0;
        var advisorCost = 0m;
        var replace = new TransitionCounter();
        var merge = new TransitionCounter();

        foreach (var set in cases)
        {
            var name = set.Case.Name;
            if (set.Cheap.Count != set.Strong.Count || set.Cheap.Count != set.Advisor.Count)
                throw new InvalidOperationException(
                    $"Case '{name}' has {set.Cheap.Count} cheap, {set.Strong.Count} strong and {set.Advisor.Count} advisor round(s); the passes must use the same number of rounds.");

            var merged = new List<ReviewResult>();
            var caseEscalated = 0;
            for (var i = 0; i < set.Cheap.Count; i++)
            {
                var final = set.Advisor[i];
                var trace = final.Advisor
                    ?? throw new InvalidOperationException(
                        $"Round {i} of case '{name}' in the advisor pass has no advisor trace, so it was not made by the advisor pattern.");

                rounds++;
                if (!trace.Escalated)
                {
                    merged.Add(final);
                    continue;
                }

                escalated++;
                caseEscalated++;
                advisorCost += trace.Cost;
                if (trace.Failed)
                    advisorFailed++;
                else if (trace.Hints == 0)
                    zeroHints++;

                var mergedReview = final with { Findings = MergeFindings(set.Cheap[i], final) };
                merged.Add(mergedReview);

                var before = GoldenScorer.Succeeded(set.Cheap[i].Findings ?? [], set.Case.Expect);
                replace.Count(set.Case.Expect, before, GoldenScorer.Succeeded(final.Findings ?? [], set.Case.Expect));
                merge.Count(set.Case.Expect, before, GoldenScorer.Succeeded(mergedReview.Findings ?? [], set.Case.Expect));
            }

            lines.Add(new AdvisorCaseLine(
                name,
                set.Case.Expect is ExpectNoFinding ? GoldenKind.Trap : GoldenKind.Detection,
                caseEscalated,
                CaseColumn(set.Case, promptVersion, set.Cheap),
                CaseColumn(set.Case, promptVersion, set.Strong),
                CaseColumn(set.Case, promptVersion, set.Advisor),
                CaseColumn(set.Case, promptVersion, merged)));
        }

        return new AdvisorComparison(
            lines,
            Column("Cheap alone", lines, l => l.Cheap, cases.SelectMany(c => c.Cheap)),
            Column("Strong alone", lines, l => l.Strong, cases.SelectMany(c => c.Strong)),
            Column("Advisor, replace", lines, l => l.Replace, cases.SelectMany(c => c.Advisor)),
            Column("Advisor, merge", lines, l => l.Merge, cases.SelectMany(c => c.Advisor)),
            rounds, escalated, zeroHints, advisorFailed, advisorCost, replace.Result, merge.Result);
    }

    /// <summary>
    /// The first review's findings, then each of the final review's findings the first one does not
    /// already have. Two findings are the same when they sit on the same file and cite the same snippet.
    /// </summary>
    public static List<Finding> MergeFindings(ReviewResult first, ReviewResult final)
    {
        var firstFindings = first.Findings ?? [];
        return
        [
            .. firstFindings,
            .. (final.Findings ?? []).Where(f => !firstFindings.Any(g => SameSpot(f, g))),
        ];
    }

    private static bool SameSpot(Finding a, Finding b) =>
        string.Equals(a.File?.Trim(), b.File?.Trim(), StringComparison.Ordinal)
        && string.Equals(a.CodeSnippet?.Trim(), b.CodeSnippet?.Trim(), StringComparison.Ordinal);

    private static AdvisorCaseColumn CaseColumn(
        GoldenCase golden, string promptVersion, IReadOnlyList<ReviewResult> reviews) =>
        new(GoldenEvaluator.ScoreOneSide(golden, promptVersion, reviews).Result, reviews.Sum(r => r.Cost));

    private static AdvisorColumn Column(
        string label, IReadOnlyList<AdvisorCaseLine> lines, Func<AdvisorCaseLine, AdvisorCaseColumn> pick,
        IEnumerable<ReviewResult> reviews)
    {
        var results = lines.Select(l => pick(l).Result).ToList();
        var all = reviews.ToList();
        return new AdvisorColumn(label, results, GoldenGates.Judge(results), all.Sum(r => r.Cost), all.Sum(r => r.LatencyMs));
    }

    private sealed class TransitionCounter
    {
        private int _detectionGained, _detectionLost, _trapGained, _trapLost;

        public AdvisorTransitions Result =>
            new(new Transitions(_detectionGained, _detectionLost), new Transitions(_trapGained, _trapLost));

        public void Count(GoldenExpectation expect, bool before, bool after)
        {
            if (before == after)
                return;

            if (expect is ExpectNoFinding)
            {
                if (after)
                    _trapGained++;
                else
                    _trapLost++;
            }
            else if (after)
                _detectionGained++;
            else
                _detectionLost++;
        }
    }
}
