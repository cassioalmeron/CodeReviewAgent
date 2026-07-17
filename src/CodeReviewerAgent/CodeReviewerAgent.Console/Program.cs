using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

// `judge <id>` — evaluate a stored analysis with the LLM-as-judge, saving the scores (Route B).
if (args is ["judge", var judgeIdArg, ..] && int.TryParse(judgeIdArg, out var judgeAnalysisId))
{
    var store = RepositoryFactory.Create();
    var analysis = store.Analyses.Get(judgeAnalysisId)
        ?? throw new InvalidOperationException($"No analysis with id {judgeAnalysisId}.");
    var judgedDiff = store.Diffs.Get(analysis.DiffId)
        ?? throw new InvalidOperationException($"No diff with id {analysis.DiffId}.");

    var judgeModel = Environment.GetEnvironmentVariable("JUDGE_MODEL") ?? "claude-sonnet-4-6";
    var rubricVersion = Environment.GetEnvironmentVariable("RUBRIC_VERSION") ?? "v1";
    var judge = new Judge(LlmClientFactory.CreateClaude(judgeModel), rubricVersion);
    var outcome = judge.Evaluate(judgedDiff.Content, ToReviewResult(analysis, judgedDiff.Content));

    var evaluationId = store.JudgeEvaluations.Save(
        ToJudgeEvaluation(judgeAnalysisId, judgeModel, rubricVersion, outcome));
    Console.WriteLine($"Judge evaluation saved with id {evaluationId} (overall {outcome.Judgment.Overall}/5)");
    return;
}

// `judge` (no id) scores the reviews persisted by `eval`; it builds its own (stronger) client.
if (args is ["judge", ..])
{
    JudgeRunner.Run();
    return;
}

// `eval` runs the golden set (detection only); `all` also runs the judge over its results.
if (args is ["eval", ..])
{
    RunGoldenSet(LlmClientFactory.Create());
    return;
}

if (args is ["all", ..])
{
    var executor = LlmClientFactory.Create();
    RunGoldenSet(executor);
    JudgeRunner.Run();
    return;
}

var repos = RepositoryFactory.Create();

// `diff <source>` — capture a diff and store it, printing its id.
if (args is ["diff", .. var sourceArgs])
{
    var source = DiffSourceFactory.Create(sourceArgs);
    var id = repos.Diffs.Save(new Diff
    {
        Content = source.GetDiff(),
        Source = Label(sourceArgs),
        CreatedAt = DateTime.UtcNow,
    });
    Console.WriteLine($"Diff saved with id {id}");
    return;
}

// `review <id>` — analyze a stored diff, saving a new analysis (history of N per diff).
if (args is ["review", var reviewIdArg, ..] && int.TryParse(reviewIdArg, out var reviewId))
{
    var diff = repos.Diffs.Get(reviewId)
        ?? throw new InvalidOperationException($"No diff with id {reviewId}.");
    var result = new CodeReviewer(LlmClientFactory.Create(), diff.Content).Review();
    var analysisId = repos.Analyses.Save(Analysis.FromReview(reviewId, result));
    Console.WriteLine($"Analysis saved with id {analysisId}");
    return;
}

// `report <id>` — regenerate the Markdown report from a stored analysis, no LLM call.
if (args is ["report", var reportIdArg, ..] && int.TryParse(reportIdArg, out var reportId))
{
    var analysis = repos.Analyses.Get(reportId)
        ?? throw new InvalidOperationException($"No analysis with id {reportId}.");
    var diff = repos.Diffs.Get(analysis.DiffId)
        ?? throw new InvalidOperationException($"No diff with id {analysis.DiffId}.");
    var path = ReportGenerator.Save(ToReviewResult(analysis, diff.Content));
    Console.WriteLine($"Report saved to {path}");
    return;
}

// `judge-report golden` — one consolidated report over the golden set's evaluations (diffs whose
// Source is "golden:*"), grouped by case with per-group averages and overall totals, like the old
// golden judge report. Ad-hoc review/judge records are excluded.
if (args is ["judge-report", "golden", ..])
{
    var analysesById = repos.Analyses.List().ToDictionary(a => a.Id);
    var diffsById = repos.Diffs.List().ToDictionary(d => d.Id);

    var evaluations = repos.JudgeEvaluations.List()
        .Where(e => (diffsById[analysesById[e.AnalysisId].DiffId].Source ?? "").StartsWith("golden:"))
        .ToList();
    if (evaluations.Count == 0)
        throw new InvalidOperationException("No golden judge evaluations stored. Run `eval` then judge them.");

    var groups = evaluations
        .GroupBy(e => analysesById[e.AnalysisId].DiffId)
        .Select(g => new JudgeReportGroup(
            DiffLabel(diffsById[g.Key].Content), diffsById[g.Key].Content, g.Select(ToOutcome).ToList()))
        .ToList();

    var path = JudgeReportGenerator.Save(groups, HeaderValue(evaluations.Select(e => e.JudgeModel)),
        HeaderValue(evaluations.Select(e => e.RubricVersion)));
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// `judge-report analysisId <n>` — judge report for every evaluation of analysis n (no LLM).
if (args is ["judge-report", "analysisId", var byAnalysisArg, ..] && int.TryParse(byAnalysisArg, out var byAnalysisId))
{
    var analysis = repos.Analyses.Get(byAnalysisId)
        ?? throw new InvalidOperationException($"No analysis with id {byAnalysisId}.");
    var diff = repos.Diffs.Get(analysis.DiffId)
        ?? throw new InvalidOperationException($"No diff with id {analysis.DiffId}.");
    var evaluations = repos.JudgeEvaluations.List().Where(e => e.AnalysisId == byAnalysisId).ToList();
    if (evaluations.Count == 0)
        throw new InvalidOperationException($"No judge evaluations for analysis {byAnalysisId}.");
    var path = JudgeReportGenerator.SaveForAnalysis(byAnalysisId, diff.Content, evaluations);
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// `judge-report <evaluationId>` — judge report for a single evaluation (no LLM).
if (args is ["judge-report", var evaluationIdArg, ..] && int.TryParse(evaluationIdArg, out var singleEvaluationId))
{
    var evaluation = repos.JudgeEvaluations.Get(singleEvaluationId)
        ?? throw new InvalidOperationException($"No judge evaluation with id {singleEvaluationId}.");
    var analysis = repos.Analyses.Get(evaluation.AnalysisId)
        ?? throw new InvalidOperationException($"No analysis with id {evaluation.AnalysisId}.");
    var diff = repos.Diffs.Get(analysis.DiffId)
        ?? throw new InvalidOperationException($"No diff with id {analysis.DiffId}.");
    var path = JudgeReportGenerator.SaveForAnalysis(evaluation.AnalysisId, diff.Content, [evaluation]);
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// Combined convenience (backward compatible): capture + review + report in one shot.
// `pr <n> --publish` still posts the findings to the PR.
var client = LlmClientFactory.Create();
var diffSource = DiffSourceFactory.Create(args);
try
{
    var content = diffSource.GetDiff();
    var diffId = repos.Diffs.Save(new Diff
    {
        Content = content,
        Source = Label(args),
        CreatedAt = DateTime.UtcNow,
    });

    var review = CodeReviewer.ReviewAndReport(client, content);
    repos.Analyses.Save(Analysis.FromReview(diffId, review));

    if (args is ["pr", var prArg, ..] && args.Contains("--publish") && int.TryParse(prArg, out var prNumber))
        PrPublisher.Publish(prNumber, review);
}
catch (Exception ex) when (args is ["pr", ..])
{
    // Graceful skip: the agent must never block the PR. Log to stderr and exit cleanly.
    Console.Error.WriteLine($"Review skipped due to an error: {ex.Message}");
}

static void RunGoldenSet(ILlmClient executor)
{
    var repos = RepositoryFactory.Create();
    var results = GoldenEvaluator.Run(executor, repos.Diffs, repos.Analyses);
    Console.WriteLine();
    Console.WriteLine("=== Golden set ===");
    foreach (var r in results)
        Console.WriteLine(GoldenEvaluator.FormatLine(r));
    Console.WriteLine($"Golden set: {results.Sum(r => r.Detections)}/{results.Sum(r => r.Runs)} detections");
}

static string Label(string[] sourceArgs) =>
    sourceArgs.Length == 0 ? "local" : string.Join(" ", sourceArgs);

static ReviewResult ToReviewResult(Analysis a, string diff) => new(
    a.Summary, a.Findings, a.Engine, a.Model, a.PromptVersion,
    a.Cost, a.LatencyMs, a.InputTokens, a.OutputTokens, diff);

static JudgeOutcome ToOutcome(JudgeEvaluation e) => new(
    new Judgment(e.Correctness, e.Actionability, e.Calibration, e.SignalToNoise, e.Overall, e.Rationale),
    e.Cost, e.LatencyMs, e.InputTokens, e.OutputTokens);

// The first added file in the diff, used as a readable case label.
static string DiffLabel(string? diff)
{
    foreach (var line in (diff ?? "").Replace("\r\n", "\n").Split('\n'))
        if (line.StartsWith("+++ "))
        {
            var path = line[4..].Trim();
            return path.StartsWith("b/") ? path[2..] : path;
        }
    return "(unknown)";
}

// A single header value when every evaluation agrees, else "mixed".
static string HeaderValue(IEnumerable<string?> values)
{
    var distinct = values.Distinct().ToList();
    return distinct.Count == 1 ? distinct[0] ?? "—" : "mixed";
}

static JudgeEvaluation ToJudgeEvaluation(int analysisId, string judgeModel, string rubricVersion, JudgeOutcome o) => new()
{
    AnalysisId = analysisId,
    RubricVersion = rubricVersion,
    JudgeModel = judgeModel,
    Correctness = o.Judgment.Correctness,
    Actionability = o.Judgment.Actionability,
    Calibration = o.Judgment.Calibration,
    SignalToNoise = o.Judgment.SignalToNoise,
    Overall = o.Judgment.Overall,
    Rationale = o.Judgment.Rationale,
    Cost = o.Cost,
    LatencyMs = o.LatencyMs,
    InputTokens = o.InputTokens,
    OutputTokens = o.OutputTokens,
    CreatedAt = DateTime.UtcNow,
};
