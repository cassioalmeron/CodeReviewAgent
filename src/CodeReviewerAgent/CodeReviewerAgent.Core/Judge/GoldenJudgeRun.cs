namespace CodeReviewerAgent.Core.Judge;

/// <summary>What one <see cref="GoldenJudgeRun.Run"/> found and did.</summary>
public record GoldenJudgeRunSummary(int Assessments, int JudgmentsMade, int JudgmentsAlreadyStored);

/// <summary>
/// Judges every assessment of one golden run, <c>repeat</c> times each, saving an
/// <see cref="Evaluation"/> as each judgment returns. An assessment that already holds enough
/// evaluations under the same pattern, rubric version and judge model is not judged again, so a run
/// that was interrupted (or repeated) resumes instead of paying twice.
/// <para>
/// With <c>onePerCase</c> only one assessment per distinct review is judged: the one with the lowest
/// id, so a resumed or repeated measurement always picks the same ones. Judging several rounds of
/// one case would measure how the judge reacts to different reviews, not how repeatable it is.
/// </para>
/// </summary>
public static class GoldenJudgeRun
{
    /// <param name="judgeFactory">
    /// Called at most once, and only when there is something left to judge, so a run with no
    /// assessments, or with everything already stored, never builds a client.
    /// </param>
    public static GoldenJudgeRunSummary Run(
        int runId,
        int repeat,
        JudgePattern pattern,
        string rubricVersion,
        string judgeModel,
        RepositoryContext repositories,
        Func<Judge> judgeFactory,
        IProgress<string>? progress = null,
        bool onePerCase = false)
    {
        var runAssessments = repositories.Assessments.List().Where(a => a.RunId == runId).OrderBy(a => a.Id).ToList();
        var assessments = onePerCase
            ? runAssessments.GroupBy(a => a.ReviewId).Select(g => g.First()).OrderBy(a => a.Id).ToList()
            : runAssessments;
        if (runAssessments.Count > 0)
            progress?.Report($"Selected {assessments.Count} of {runAssessments.Count} assessments of run {runId}.");

        var storedCounts = repositories.Evaluations.List()
            .Where(e => e.Pattern == pattern && e.RubricVersion == rubricVersion && e.JudgeModel == judgeModel)
            .GroupBy(e => e.AssessmentId)
            .ToDictionary(g => g.Key, g => g.Count());

        Judge? judge = null;
        var made = 0;
        var alreadyStored = 0;

        foreach (var assessment in assessments)
        {
            var stored = storedCounts.GetValueOrDefault(assessment.Id);
            var missing = Math.Max(0, repeat - stored);
            alreadyStored += repeat - missing;

            if (missing == 0)
            {
                progress?.Report($"Assessment {assessment.Id}: already judged {stored}x, skipped.");
                continue;
            }

            var review = repositories.Reviews.Get(assessment.ReviewId)
                ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
            var result = new ReviewResult(assessment.Summary, assessment.Findings);

            for (var i = 0; i < missing; i++)
            {
                judge ??= judgeFactory();
                var outcome = judge.Evaluate(review.Content, result);
                var evaluationId = repositories.Evaluations.Save(new Evaluation
                {
                    AssessmentId = assessment.Id,
                    RubricVersion = rubricVersion,
                    JudgeModel = judgeModel,
                    Correctness = outcome.Judgment.Correctness,
                    Actionability = outcome.Judgment.Actionability,
                    Calibration = outcome.Judgment.Calibration,
                    SignalToNoise = outcome.Judgment.SignalToNoise,
                    Overall = outcome.Judgment.Overall,
                    Rationale = outcome.Judgment.Rationale,
                    Pattern = pattern,
                    Cost = outcome.Cost,
                    LatencyMs = outcome.LatencyMs,
                    InputTokens = outcome.InputTokens,
                    OutputTokens = outcome.OutputTokens,
                    CreatedAt = DateTime.UtcNow,
                });
                made++;
                progress?.Report(
                    $"Assessment {assessment.Id}: evaluation {evaluationId} saved ({stored + i + 1}/{repeat}, overall {outcome.Judgment.Overall}/5, {Utils.Money(outcome.Cost)}).");
            }
        }

        return new GoldenJudgeRunSummary(assessments.Count, made, alreadyStored);
    }
}
