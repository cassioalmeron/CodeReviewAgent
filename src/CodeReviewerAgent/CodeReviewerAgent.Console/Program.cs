using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

// Dispatch only: every command's body is a method below, in the same order as the branches here.
// Order matters where patterns overlap — the id-bearing forms of `judge` and `judge-report` have to
// be tested before their catch-alls.

if (args.Contains("--json"))
{
    RunJson(args);
    return;
}

// `judge <id>` — evaluate a stored assessment with the LLM-as-judge, saving the scores (Route B).
if (args is ["judge", var judgeIdArg, ..] && int.TryParse(judgeIdArg, out var judgeAssessmentId))
{
    JudgeAssessment(judgeAssessmentId);
    return;
}

// `judge` (no id) scores the reviews persisted by `eval`; it builds its own (stronger) client.
if (args is ["judge", ..])
{
    JudgeRunner.Run(JudgeClient());
    return;
}

// `eval` runs the golden set (detection + traps); `eval <name>[,<name>]` narrows to named cases —
// the tight loop for tuning a prompt or a skill without paying for a full pass.
if (args is ["eval", ..])
{
    RunGoldenSet(LlmClientFactory.Create(), args.Length > 1 ? args[1] : null);
    return;
}

// `all` — the golden set plus the judge over its results.
if (args is ["all", ..])
{
    RunAll();
    return;
}

// `skills` — list the discovered catalog (tier 1) and the validation diagnostics. No LLM call.
if (args is ["skills"])
{
    ListSkills();
    return;
}

// `skills-eval` — trigger eval: does the selector pick the right skills? Runs only the selection
// step over the labelled diffs of evals/triggers/cases.json, so it costs a fraction of a review.
// Honours SKILLS, so `SKILLS=globs` scores the mechanical strategy on the same cases for free.
if (args is ["skills-eval", ..])
{
    RunSkillTriggerEval();
    return;
}

// `skills <name>` — print the exact block this skill injects into the review prompt. No LLM call.
if (args is ["skills", var skillName])
{
    ShowSkill(skillName);
    return;
}

// `projects` — list the stored projects.
if (args is ["projects", ..])
{
    ListProjects();
    return;
}

// `project rename <id> <name...>` — rename a project (the Api is read-only, so renaming is via CLI).
if (args is ["project", "rename", var renameIdArg, .. var nameParts] && int.TryParse(renameIdArg, out var renameId))
{
    RenameProject(renameId, nameParts);
    return;
}

// `review <source>` — capture a diff for the current project and store it, printing its id.
if (args is ["review", .. var sourceArgs])
{
    CaptureReview(sourceArgs);
    return;
}

// `assess <id>` — analyze a stored review, saving a new assessment (history of N per review).
if (args is ["assess", var assessIdArg, ..] && int.TryParse(assessIdArg, out var assessReviewId))
{
    AssessReview(assessReviewId);
    return;
}

// `report <id>` — regenerate the Markdown report from a stored assessment, no LLM call.
if (args is ["report", var reportIdArg, ..] && int.TryParse(reportIdArg, out var reportId))
{
    RegenerateReport(reportId);
    return;
}

// `judge-report golden` — one consolidated report over the golden set's evaluations.
if (args is ["judge-report", "golden", ..])
{
    GoldenJudgeReport();
    return;
}

// `judge-report assessmentId <n>` — judge report for every evaluation of assessment n (no LLM).
if (args is ["judge-report", "assessmentId", var byAssessmentArg, ..] && int.TryParse(byAssessmentArg, out var byAssessmentId))
{
    AssessmentJudgeReport(byAssessmentId);
    return;
}

// `judge-report <evaluationId>` — judge report for a single evaluation (no LLM).
if (args is ["judge-report", var evaluationIdArg, ..] && int.TryParse(evaluationIdArg, out var singleEvaluationId))
{
    EvaluationJudgeReport(singleEvaluationId);
    return;
}

// No command matched: treat the args as a diff source and run the whole flow.
ReviewAndPublish(args);

// ---------------------------------------------------------------------------------------------
// Commands
// ---------------------------------------------------------------------------------------------

// `<source> --json` — review a diff and print its findings as JSON to stdout, for the
// Claude Code plugin. No report file; the pipeline's human progress is redirected to stderr so
// stdout carries pure JSON the caller can parse.
static void RunJson(string[] args)
{
    var sourceArgs = args.Where(a => a != "--json").ToArray();

    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    // `<assessmentId> --json` — return a stored assessment as JSON (no LLM call, no persistence),
    // in the same shape as a fresh review so the plugin renders it identically.
    if (sourceArgs is [var idArg] && int.TryParse(idArg, out var storedAssessmentId))
    {
        PrintStoredAssessment(storedAssessmentId, options);
        return;
    }

    var diff = DiffSourceFactory.Create(sourceArgs).GetDiff();

    // Redirect the pipeline's human progress to stderr so stdout stays pure JSON; the review and
    // assessment are persisted to the configured store (STORAGE) just like the interactive flow.
    var realOut = Console.Out;
    Console.SetOut(Console.Error);
    var repos = RepositoryFactory.Create();
    var project = ProjectResolver.Resolve(repos.Projects);
    var review = new CodeReviewer(LlmClientFactory.Create(), diff).Review();
    var reviewId = repos.Reviews.GetOrAdd(new Review
    {
        ProjectId = project.Id,
        Content = diff,
        Source = Label(sourceArgs),
        CreatedAt = DateTime.UtcNow,
    });
    var assessmentId = repos.Assessments.Save(Assessment.FromReview(reviewId, review));
    Console.SetOut(realOut);

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            reviewId,
            assessmentId,
            source = Label(sourceArgs),
            engine = review.Engine,
            model = review.Model,
            promptVersion = review.PromptVersion,
            skills = review.Skills,
            files = ReviewedFiles(review.Diff),
            summary = review.Summary,
            findings = review.Findings,
        }, options));
}

static void PrintStoredAssessment(int assessmentId, JsonSerializerOptions options)
{
    var store = RepositoryFactory.Create();
    var assessment = store.Assessments.Get(assessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {assessmentId}.");
    var review = store.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");

    Console.WriteLine(JsonSerializer.Serialize(
        new
        {
            reviewId = assessment.ReviewId,
            assessmentId = assessment.Id,
            source = review.Source,
            engine = assessment.Engine,
            model = assessment.Model,
            promptVersion = assessment.PromptVersion,
            skills = assessment.Skills,
            files = ReviewedFiles(review.Content),
            summary = assessment.Summary,
            findings = assessment.Findings,
        }, options));
}

static void JudgeAssessment(int assessmentId)
{
    var store = RepositoryFactory.Create();
    var assessment = store.Assessments.Get(assessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {assessmentId}.");
    var review = store.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");

    var judgeModel = Environment.GetEnvironmentVariable("JUDGE_MODEL") ?? "claude-sonnet-4-6";
    var rubricVersion = Environment.GetEnvironmentVariable("RUBRIC_VERSION") ?? "v1";
    var judge = new Judge(LlmClientFactory.CreateClaude(judgeModel), rubricVersion);
    var outcome = judge.Evaluate(review.Content, ToReviewResult(assessment, review.Content));

    var evaluationId = store.Evaluations.Save(ToEvaluation(assessmentId, judgeModel, rubricVersion, outcome));
    Console.WriteLine($"Evaluation saved with id {evaluationId} (overall {outcome.Judgment.Overall}/5)");
}

static void RunAll()
{
    var executor = LlmClientFactory.Create();
    RunGoldenSet(executor);
    JudgeRunner.Run(JudgeClient());
}

static void RunGoldenSet(ILlmClient executor, string? filter = null)
{
    var repos = RepositoryFactory.Create();
    var run = GoldenEvaluator.Run(executor, repos.Projects, repos.Reviews, repos.Assessments, filter);

    // Running the set is free of I/O; publishing the result is this layer's job.
    GoldenEvaluator.SaveReport(run);

    Console.WriteLine();
    Console.WriteLine("=== Golden set ===");
    foreach (var r in run.Results)
        Console.WriteLine(GoldenEvaluator.FormatLine(r));
    Console.WriteLine(GoldenEvaluator.FormatTotals(run.Results));
}

static void ListSkills()
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
}

static void ShowSkill(string skillName)
{
    var (catalog, _) = SkillCatalog.Discover();
    var skill = catalog.FirstOrDefault(s => s.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"No skill named '{skillName}'. Run `skills` to list them.");
    Console.Write(SkillCatalog.Activate(skill).Render());
}

static void RunSkillTriggerEval()
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
}

static void ListProjects()
{
    var projects = RepositoryFactory.Create().Projects.List();
    if (projects.Count == 0)
        Console.WriteLine("No projects stored yet.");
    foreach (var p in projects)
        Console.WriteLine($"#{p.Id}  {p.Name}  ({p.Folder})");
}

static void RenameProject(int projectId, string[] nameParts)
{
    if (nameParts.Length == 0)
        throw new InvalidOperationException("Usage: project rename <id> <name>");

    var name = string.Join(" ", nameParts);
    RepositoryFactory.Create().Projects.Rename(projectId, name);
    Console.WriteLine($"Project {projectId} renamed to \"{name}\"");
}

static void CaptureReview(string[] sourceArgs)
{
    var repos = RepositoryFactory.Create();
    var project = ProjectResolver.Resolve(repos.Projects);
    var id = repos.Reviews.GetOrAdd(new Review
    {
        ProjectId = project.Id,
        Content = DiffSourceFactory.Create(sourceArgs).GetDiff(),
        Source = Label(sourceArgs),
        CreatedAt = DateTime.UtcNow,
    });
    Console.WriteLine($"Review saved with id {id}");
}

static void AssessReview(int reviewId)
{
    var repos = RepositoryFactory.Create();
    var review = repos.Reviews.Get(reviewId)
        ?? throw new InvalidOperationException($"No review with id {reviewId}.");
    var result = new CodeReviewer(LlmClientFactory.Create(), review.Content).Review();
    var assessmentId = repos.Assessments.Save(Assessment.FromReview(reviewId, result));
    Console.WriteLine($"Assessment saved with id {assessmentId}");
}

static void RegenerateReport(int assessmentId)
{
    var repos = RepositoryFactory.Create();
    var assessment = repos.Assessments.Get(assessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {assessmentId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
    var path = ReportGenerator.Save(ToReviewResult(assessment, review.Content));
    Console.WriteLine($"Report saved to {path}");
}

// One consolidated report over the evaluations of the "Golden Set" project, grouped by case with
// per-group averages and overall totals. Ad-hoc review/judge records of other projects are excluded.
static void GoldenJudgeReport()
{
    var repos = RepositoryFactory.Create();
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
}

static void AssessmentJudgeReport(int assessmentId)
{
    var repos = RepositoryFactory.Create();
    var assessment = repos.Assessments.Get(assessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {assessmentId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");
    var evaluations = repos.Evaluations.List().Where(e => e.AssessmentId == assessmentId).ToList();
    if (evaluations.Count == 0)
        throw new InvalidOperationException($"No judge evaluations for assessment {assessmentId}.");

    var path = JudgeReportGenerator.SaveForAssessment(assessmentId, review.Content, evaluations);
    Console.WriteLine($"Judge report saved to {path}");
}

static void EvaluationJudgeReport(int evaluationId)
{
    var repos = RepositoryFactory.Create();
    var evaluation = repos.Evaluations.Get(evaluationId)
        ?? throw new InvalidOperationException($"No evaluation with id {evaluationId}.");
    var assessment = repos.Assessments.Get(evaluation.AssessmentId)
        ?? throw new InvalidOperationException($"No assessment with id {evaluation.AssessmentId}.");
    var review = repos.Reviews.Get(assessment.ReviewId)
        ?? throw new InvalidOperationException($"No review with id {assessment.ReviewId}.");

    var path = JudgeReportGenerator.SaveForAssessment(evaluation.AssessmentId, review.Content, [evaluation]);
    Console.WriteLine($"Judge report saved to {path}");
}

// Combined convenience (backward compatible): capture + assess + report in one shot.
// `pr <n> --publish` still posts the findings to the PR.
static void ReviewAndPublish(string[] args)
{
    var repos = RepositoryFactory.Create();
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
}

// ---------------------------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------------------------

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
