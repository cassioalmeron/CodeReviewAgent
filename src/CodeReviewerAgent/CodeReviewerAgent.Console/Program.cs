using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

// `<source> --json` — review a diff and print its findings as JSON to stdout, for the
// Claude Code plugin. No persistence and no report file; the pipeline's human progress is
// redirected to stderr so stdout carries pure JSON the caller can parse.
if (args.Contains("--json"))
{
    var jsonSourceArgs = args.Where(a => a != "--json").ToArray();

    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    // `<assessmentId> --json` — return a stored assessment as JSON (no LLM call, no persistence),
    // in the same shape as a fresh review so the plugin renders it identically.
    if (jsonSourceArgs is [var jsonIdArg] && int.TryParse(jsonIdArg, out var storedAssessmentId))
    {
        var jsonStore = RepositoryFactory.Create();
        var storedAssessment = jsonStore.Assessments.Get(storedAssessmentId)
            ?? throw new InvalidOperationException($"No assessment with id {storedAssessmentId}.");
        var storedReview = jsonStore.Reviews.Get(storedAssessment.ReviewId)
            ?? throw new InvalidOperationException($"No review with id {storedAssessment.ReviewId}.");
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                reviewId = storedAssessment.ReviewId,
                assessmentId = storedAssessment.Id,
                source = storedReview.Source,
                engine = storedAssessment.Engine,
                model = storedAssessment.Model,
                promptVersion = storedAssessment.PromptVersion,
                skills = storedAssessment.Skills,
                files = ReviewedFiles(storedReview.Content),
                summary = storedAssessment.Summary,
                findings = storedAssessment.Findings,
            }, jsonOptions));
        return;
    }

    var jsonDiff = DiffSourceFactory.Create(jsonSourceArgs).GetDiff();

    // Redirect the pipeline's human progress to stderr so stdout stays pure JSON; the review and
    // assessment are persisted to the configured store (STORAGE) just like the interactive flow.
    var realOut = Console.Out;
    Console.SetOut(Console.Error);
    var jsonRepos = RepositoryFactory.Create();
    var jsonProject = ProjectResolver.Resolve(jsonRepos.Projects);
    var jsonReview = new CodeReviewer(LlmClientFactory.Create(), jsonDiff).Review();
    var jsonReviewId = jsonRepos.Reviews.GetOrAdd(new Review
    {
        ProjectId = jsonProject.Id,
        Content = jsonDiff,
        Source = Label(jsonSourceArgs),
        CreatedAt = DateTime.UtcNow,
    });
    var jsonAssessmentId = jsonRepos.Assessments.Save(Assessment.FromReview(jsonReviewId, jsonReview));
    Console.SetOut(realOut);

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            reviewId = jsonReviewId,
            assessmentId = jsonAssessmentId,
            source = Label(jsonSourceArgs),
            engine = jsonReview.Engine,
            model = jsonReview.Model,
            promptVersion = jsonReview.PromptVersion,
            skills = jsonReview.Skills,
            files = ReviewedFiles(jsonReview.Diff),
            summary = jsonReview.Summary,
            findings = jsonReview.Findings,
        }, jsonOptions));
    return;
}

// `judge <id>` — evaluate a stored assessment with the LLM-as-judge, saving the scores (Route B).
if (args is ["judge", var judgeIdArg, ..] && int.TryParse(judgeIdArg, out var judgeAssessmentId))
{
    var store = RepositoryFactory.Create();
    var assessment = store.Assessments.Get(judgeAssessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {judgeAssessmentId}.");
    var judgedReview = store.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");

    var judgeModel = Environment.GetEnvironmentVariable("JUDGE_MODEL") ?? "claude-sonnet-4-6";
    var rubricVersion = Environment.GetEnvironmentVariable("RUBRIC_VERSION") ?? "v1";
    var judge = new Judge(LlmClientFactory.CreateClaude(judgeModel), rubricVersion);
    var outcome = judge.Evaluate(judgedReview.Content, ToReviewResult(assessment, judgedReview.Content));

    var evaluationId = store.Evaluations.Save(
        ToEvaluation(judgeAssessmentId, judgeModel, rubricVersion, outcome));
    Console.WriteLine($"Evaluation saved with id {evaluationId} (overall {outcome.Judgment.Overall}/5)");
    return;
}

// `judge` (no id) scores the reviews persisted by `eval`; it builds its own (stronger) client.
if (args is ["judge", ..])
{
    JudgeRunner.Run(JudgeClient());
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
    JudgeRunner.Run(JudgeClient());
    return;
}

// `skills` — list the discovered catalog (tier 1) and the validation diagnostics. No LLM call.
if (args is ["skills"])
{
    var (catalog, diagnostics) = SkillCatalog.Discover();
    if (catalog.Count == 0)
        Console.WriteLine("No skills discovered.");
    foreach (var skill in catalog)
    {
        Console.WriteLine($"{skill.Name}  ({skill.Location})");
        Console.WriteLine($"  {skill.Description}");
    }
    if (diagnostics.Count > 0)
        Console.WriteLine();
    foreach (var diagnostic in diagnostics)
        Console.WriteLine($"[{diagnostic.Level}] {diagnostic.Path}: {diagnostic.Message}");
    return;
}

// `skills-eval` — trigger eval: does the selector pick the right skills? Runs only the selection
// step over the labelled diffs of evaluations/triggers/cases.json, so it costs a fraction of a review.
// Honours SKILLS, so `SKILLS=globs` scores the mechanical strategy on the same cases for free.
if (args is ["skills-eval", ..])
{
    var selector = SkillSelectorFactory.Create(LlmClientFactory.Create());
    var names = SkillCatalog.Discover().Skills.Select(s => s.Name).ToList();
    var results = SkillTriggerEvaluator.Run(selector);

    Console.WriteLine();
    Console.WriteLine("=== Skill trigger eval ===");
    foreach (var result in results)
        Console.WriteLine(SkillTriggerEvaluator.FormatLine(result, names));
    foreach (var set in results.Select(r => r.Set).Distinct())
    {
        var inSet = results.Where(r => r.Set == set).ToList();
        Console.WriteLine($"{set}: {inSet.Count(r => r.Passed(names))}/{inSet.Count} passed");
    }
    Console.WriteLine(SkillTriggerEvaluator.FormatTotals(results));
    Console.WriteLine($"Report saved to {SkillTriggerEvaluator.Save(results, names)}");
    return;
}

// `skills <name>` — print the exact block this skill injects into the review prompt. No LLM call.
if (args is ["skills", var skillName])
{
    var (catalog, _) = SkillCatalog.Discover();
    var skill = catalog.FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No skill named '{skillName}'. Run `skills` to list them.");
    Console.Write(SkillCatalog.Activate(skill).Render());
    return;
}

var repos = RepositoryFactory.Create();

// `projects` — list the stored projects.
if (args is ["projects", ..])
{
    var projects = repos.Projects.List();
    if (projects.Count == 0)
        Console.WriteLine("No projects stored yet.");
    foreach (var p in projects)
        Console.WriteLine($"#{p.Id}  {p.Name}  ({p.Folder})");
    return;
}

// `project rename <id> <name...>` — rename a project (the Api is read-only, so renaming is via CLI).
if (args is ["project", "rename", var renameIdArg, .. var nameParts] && int.TryParse(renameIdArg, out var renameId))
{
    if (nameParts.Length == 0)
        throw new InvalidOperationException("Usage: project rename <id> <name>");
    repos.Projects.Rename(renameId, string.Join(" ", nameParts));
    Console.WriteLine($"Project {renameId} renamed to \"{string.Join(" ", nameParts)}\"");
    return;
}

// `review <source>` — capture a diff for the current project and store it, printing its id.
if (args is ["review", .. var sourceArgs])
{
    var project = ProjectResolver.Resolve(repos.Projects);
    var source = DiffSourceFactory.Create(sourceArgs);
    var id = repos.Reviews.GetOrAdd(new Review
    {
        ProjectId = project.Id,
        Content = source.GetDiff(),
        Source = Label(sourceArgs),
        CreatedAt = DateTime.UtcNow,
    });
    Console.WriteLine($"Review saved with id {id}");
    return;
}

// `assess <id>` — analyze a stored review, saving a new assessment (history of N per review).
if (args is ["assess", var assessIdArg, ..] && int.TryParse(assessIdArg, out var assessReviewId))
{
    var review = repos.Reviews.Get(assessReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessReviewId}.");
    var result = new CodeReviewer(LlmClientFactory.Create(), review.Content).Review();
    var assessmentId = repos.Assessments.Save(Assessment.FromReview(assessReviewId, result));
    Console.WriteLine($"Assessment saved with id {assessmentId}");
    return;
}

// `report <id>` — regenerate the Markdown report from a stored assessment, no LLM call.
if (args is ["report", var reportIdArg, ..] && int.TryParse(reportIdArg, out var reportId))
{
    var assessment = repos.Assessments.Get(reportId)
        ?? throw new InvalidOperationException($"No assessment with id {reportId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
    var path = ReportGenerator.Save(ToReviewResult(assessment, review.Content));
    Console.WriteLine($"Report saved to {path}");
    return;
}

// `judge-report golden` — one consolidated report over the golden set's evaluations (the reviews
// of the "Golden Set" project), grouped by case with per-group averages and overall totals. Ad-hoc
// review/judge records of other projects are excluded.
if (args is ["judge-report", "golden", ..])
{
    var goldenProject = repos.Projects.List().FirstOrDefault(p => p.Folder == "golden")
        ?? throw new InvalidOperationException("No Golden Set project stored. Run `eval` first.");

    var assessmentsById = repos.Assessments.List().ToDictionary(a => a.Id);
    var reviewsById = repos.Reviews.List().ToDictionary(r => r.Id);

    var evaluations = repos.Evaluations.List()
        .Where(e => reviewsById[assessmentsById[e.AssessmentId].ReviewId].ProjectId == goldenProject.Id)
        .ToList();
    if (evaluations.Count == 0)
        throw new InvalidOperationException("No golden judge evaluations stored. Run `eval` then judge them.");

    var groups = evaluations
        .GroupBy(e => assessmentsById[e.AssessmentId].ReviewId)
        .Select(g => new JudgeReportGroup(
            DiffLabel(reviewsById[g.Key].Content), reviewsById[g.Key].Content, g.Select(ToOutcome).ToList()))
        .ToList();

    var path = JudgeReportGenerator.Save(groups, HeaderValue(evaluations.Select(e => e.JudgeModel)),
        HeaderValue(evaluations.Select(e => e.RubricVersion)));
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// `judge-report assessmentId <n>` — judge report for every evaluation of assessment n (no LLM).
if (args is ["judge-report", "assessmentId", var byAssessmentArg, ..] && int.TryParse(byAssessmentArg, out var byAssessmentId))
{
    var assessment = repos.Assessments.Get(byAssessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {byAssessmentId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
    var evaluations = repos.Evaluations.List().Where(e => e.AssessmentId == byAssessmentId).ToList();
    if (evaluations.Count == 0)
        throw new InvalidOperationException($"No judge evaluations for assessment {byAssessmentId}.");
    var path = JudgeReportGenerator.SaveForAssessment(byAssessmentId, review.Content, evaluations);
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// `judge-report <evaluationId>` — judge report for a single evaluation (no LLM).
if (args is ["judge-report", var evaluationIdArg, ..] && int.TryParse(evaluationIdArg, out var singleEvaluationId))
{
    var evaluation = repos.Evaluations.Get(singleEvaluationId)
        ?? throw new InvalidOperationException($"No evaluation with id {singleEvaluationId}.");
    var assessment = repos.Assessments.Get(evaluation.AssessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {evaluation.AssessmentId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
    var path = JudgeReportGenerator.SaveForAssessment(evaluation.AssessmentId, review.Content, [evaluation]);
    Console.WriteLine($"Judge report saved to {path}");
    return;
}

// Combined convenience (backward compatible): capture + assess + report in one shot.
// `pr <n> --publish` still posts the findings to the PR.
var client = LlmClientFactory.Create();
var diffSource = DiffSourceFactory.Create(args);
try
{
    var project = ProjectResolver.Resolve(repos.Projects);
    var content = diffSource.GetDiff();
    var reviewId = repos.Reviews.GetOrAdd(new Review
    {
        ProjectId = project.Id,
        Content = content,
        Source = Label(args),
        CreatedAt = DateTime.UtcNow,
    });

    var review = CodeReviewer.ReviewAndReport(client, content);
    repos.Assessments.Save(Assessment.FromReview(reviewId, review));

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
    var results = GoldenEvaluator.Run(executor, repos.Projects, repos.Reviews, repos.Assessments);
    Console.WriteLine();
    Console.WriteLine("=== Golden set ===");
    foreach (var r in results)
        Console.WriteLine(GoldenEvaluator.FormatLine(r));
    Console.WriteLine($"Golden set: {results.Sum(r => r.Detections)}/{results.Sum(r => r.Runs)} detections");
}

// The judge uses a stronger model (JUDGE_MODEL) than the executor to avoid self-preference bias.
static ILlmClient JudgeClient() =>
    LlmClientFactory.CreateClaude(Environment.GetEnvironmentVariable("JUDGE_MODEL") ?? "claude-sonnet-4-6");

static string Label(string[] sourceArgs) =>
    sourceArgs.Length == 0 ? "local" : string.Join(" ", sourceArgs);

// The files actually reviewed, read from the diff the reviewer used (post markdown-filter),
// so the JSON caller can tell exactly what was analyzed. Deleted files (+++ /dev/null) are skipped.
static string[] ReviewedFiles(string? diff) =>
    (diff ?? "").Replace("\r\n", "\n").Split('\n')
        .Where(l => l.StartsWith("+++ "))
        .Select(l => l[4..].Trim())
        .Where(p => p != "/dev/null")
        .Select(p => p.StartsWith("b/") ? p[2..] : p)
        .Distinct()
        .ToArray();

static ReviewResult ToReviewResult(Assessment a, string diff) => new(
    a.Summary, a.Findings, a.Engine, a.Model, a.PromptVersion,
    a.Cost, a.LatencyMs, a.InputTokens, a.OutputTokens, diff, a.Skills);

static JudgeOutcome ToOutcome(Evaluation e) => new(
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

static Evaluation ToEvaluation(int assessmentId, string judgeModel, string rubricVersion, JudgeOutcome o) => new()
{
    AssessmentId = assessmentId,
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
