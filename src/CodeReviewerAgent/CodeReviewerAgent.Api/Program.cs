using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Judge;
using CodeReviewerAgent.Infra;

// The same file the CLI reads, found by walking up from the executable, so STORAGE / DB_CONNECTION
// resolve identically for both. An Api pointing at another store shows an empty screen.
EnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

// Traces + metrics over OTLP; a no-op unless OTEL_EXPORTER_OTLP_ENDPOINT is set (see Telemetry.cs).
builder.AddApiTelemetry();

const string DevCors = "dev";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Severity / Category serialize as strings ("Critical", "Bug"), matching how findings are stored.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc("v1", new() { Title = "Code Review Agent — Viewer API", Version = "v1" }));

// The schema is applied once, at startup. Every request then gets its own store, because a
// DbContext serves one operation at a time and a browser opens several requests at once: sharing one
// for the whole process is the "second operation started on this context" crash.
using (RepositoryFactory.Create()) { }
builder.Services.AddScoped(_ => RepositoryFactory.Create(applyMigrations: false));

var app = builder.Build();
app.UseCors(DevCors);

app.UseSwagger();
app.UseSwaggerUI();

var projects = app.MapGroup("/api/projects");

projects.MapGet("/", (RepositoryContext store) =>
{
    var reviews = store.Reviews.List();
    var assessments = store.Assessments.List();
    var reviewIdsByProject = reviews.ToLookup(r => r.ProjectId, r => r.Id);
    return store.Projects.List().Select(p =>
    {
        var reviewIds = reviewIdsByProject[p.Id].ToHashSet();
        var projectAssessments = assessments.Where(a => reviewIds.Contains(a.ReviewId)).ToList();
        return new ProjectListItem(
            p.Id, p.Name, p.Folder, p.CreatedAt,
            reviewIds.Count,
            projectAssessments.Count,
            projectAssessments.Count == 0 ? null : projectAssessments.Max(a => a.CreatedAt));
    });
});

projects.MapGet("/{id:int}", (int id, RepositoryContext store) =>
    store.Projects.Get(id) is { } project ? Results.Ok(project) : Results.NotFound());

projects.MapGet("/{id:int}/reviews", (int id, RepositoryContext store) =>
    store.Reviews.List().Where(r => r.ProjectId == id).Select(r => ToReviewListItem(store, r)));

// Everything the project dashboard draws, in one aggregated payload (computed in-memory over List()).
projects.MapGet("/{id:int}/stats", (int id, RepositoryContext store) =>
{
    if (store.Projects.Get(id) is null)
        return Results.NotFound();

    var reviewIds = store.Reviews.List().Where(r => r.ProjectId == id).Select(r => r.Id).ToHashSet();
    var assessments = store.Assessments.List().Where(a => reviewIds.Contains(a.ReviewId)).ToList();
    var assessmentIds = assessments.Select(a => a.Id).ToHashSet();
    var evaluations = store.Evaluations.List().Where(e => assessmentIds.Contains(e.AssessmentId)).ToList();
    var findings = assessments.SelectMany(a => a.Findings ?? []).ToList();

    // Fixed severity order for the pie/legend (Critical → Warning → Info reads worst-first).
    Severity[] severityOrder = [Severity.Critical, Severity.Warning, Severity.Info];
    var bySeverity = severityOrder
        .Select(s => new SliceCount(s.ToString(), findings.Count(f => f.Severity == s)))
        .Where(s => s.Count > 0)
        .ToList();

    var byCategory = findings
        .Where(f => f.Category is not null)
        .GroupBy(f => f.Category!.Value)
        .Select(g => new SliceCount(g.Key.ToString(), g.Count()))
        .OrderByDescending(s => s.Count)
        .ToList();

    var topFiles = findings
        .Where(f => !string.IsNullOrWhiteSpace(f.File))
        .GroupBy(f => f.File!)
        .Select(g => new SliceCount(g.Key, g.Count()))
        .OrderByDescending(s => s.Count)
        .Take(10)
        .ToList();

    var runs = assessments
        .OrderBy(a => a.CreatedAt)
        .Select(a => new AssessmentRunPoint(
            a.Id, a.CreatedAt, a.Cost, a.InputTokens, a.OutputTokens, a.LatencyMs, a.Findings?.Count ?? 0))
        .ToList();

    var judge = evaluations.Count == 0 ? null : new JudgeAverages(
        evaluations.Average(e => e.Correctness),
        evaluations.Average(e => e.Actionability),
        evaluations.Average(e => e.Calibration),
        evaluations.Average(e => e.SignalToNoise),
        evaluations.Average(e => e.Overall),
        evaluations.Count);

    // Percentiles, not an average: on the DeepSeek run the average is 2 min 17 s and half the
    // reviews answered within a minute, because a few retries after a timeout pull the mean up.
    var latencies = assessments.Select(a => a.LatencyMs).Order().ToList();

    return Results.Ok(new ProjectStats(
        id,
        reviewIds.Count, assessments.Count, evaluations.Count, findings.Count,
        assessments.Sum(a => a.Cost), assessments.Sum(a => (long)a.InputTokens),
        assessments.Sum(a => (long)a.OutputTokens),
        GoldenRunSummary.Percentile(latencies, 50),
        GoldenRunSummary.Percentile(latencies, 95),
        GoldenRunSummary.Percentile(latencies, 99),
        bySeverity, byCategory, topFiles, runs, judge));
});

var reviews = app.MapGroup("/api/reviews");

// `?projectId=N` filters to one project; without it, every review.
reviews.MapGet("/", (int? projectId, RepositoryContext store) => store.Reviews.List()
    .Where(r => projectId is null || r.ProjectId == projectId)
    .Select(r => ToReviewListItem(store, r)));

reviews.MapGet("/{id:int}", (int id, RepositoryContext store) =>
    store.Reviews.Get(id) is { } review ? Results.Ok(review) : Results.NotFound());

reviews.MapGet("/{id:int}/assessments", (int id, RepositoryContext store) =>
    store.Assessments.List().Where(a => a.ReviewId == id).Select(ToAssessmentListItem));

var assessmentsGroup = app.MapGroup("/api/assessments");

assessmentsGroup.MapGet("/", (RepositoryContext store) =>
    store.Assessments.List().Select(ToAssessmentListItem));

assessmentsGroup.MapGet("/{id:int}", (int id, RepositoryContext store) =>
    store.Assessments.Get(id) is { } assessment ? Results.Ok(assessment) : Results.NotFound());

assessmentsGroup.MapGet("/{id:int}/evaluations", (int id, RepositoryContext store) =>
    store.Evaluations.List().Where(e => e.AssessmentId == id));

// The golden runs are not under a project: a run measures a model against the golden set, and the
// set is the same one for every repository the agent reviews.
var goldenRuns = app.MapGroup("/api/golden-runs");

goldenRuns.MapGet("/", (RepositoryContext store) =>
{
    var reviewCounts = ReviewCountsByRun(store);
    return store.GoldenRuns.List()
        .OrderByDescending(r => r.StartedAt)
        .Select(r => ToGoldenRunListItem(r, reviewCounts.GetValueOrDefault(r.Id)));
});

goldenRuns.MapGet("/{id:int}", (int id, RepositoryContext store) =>
{
    if (store.GoldenRuns.Get(id) is not { } run)
        return Results.NotFound();

    return Results.Ok(new GoldenRunDetail(
        ToGoldenRunListItem(run, ReviewCountsByRun(store).GetValueOrDefault(run.Id)),
        [.. (run.Gates ?? []).Select(ToGoldenGateView)],
        [.. (run.Cases ?? []).OrderBy(c => c.Kind).ThenBy(c => c.CaseName)]));
});

var evaluations = app.MapGroup("/api/evaluations");

evaluations.MapGet("/", (RepositoryContext store) => store.Evaluations.List());

evaluations.MapGet("/{id:int}", (int id, RepositoryContext store) =>
    store.Evaluations.Get(id) is { } evaluation ? Results.Ok(evaluation) : Results.NotFound());

app.Run();

static ReviewListItem ToReviewListItem(RepositoryContext store, Review r) => new(
    r.Id, r.ProjectId, store.Projects.Get(r.ProjectId)?.Name, r.Source, r.ContentHash, r.CreatedAt,
    store.Assessments.List().Count(a => a.ReviewId == r.Id));

// How many reviews each run produced. Counted in memory over the assessments, like every other
// aggregate in this Api, because the repositories answer with lists and not with queries.
static Dictionary<int, int> ReviewCountsByRun(RepositoryContext store) => store.Assessments.List()
    .Where(a => a.RunId is not null)
    .GroupBy(a => a.RunId!.Value)
    .ToDictionary(g => g.Key, g => g.Count());

static GoldenRunListItem ToGoldenRunListItem(GoldenRun r, int reviewCount) => new(
    r.Id, r.Model, r.Engine, r.Skills, r.PromptVersion, r.StartedAt, r.DurationMs,
    r.Cost, r.InputTokens, r.OutputTokens,
    r.LatencyP50Ms, r.LatencyP95Ms, r.LatencyP99Ms,
    r.Approved, r.Cases?.Count ?? 0, r.Cases?.Count(c => c.Approved) ?? 0, reviewCount, r.ReportFile);

// The value and the floor are formatted here, once, with the same wording the markdown report uses.
static GoldenGateView ToGoldenGateView(GoldenGate stored)
{
    var gate = new Gate(stored.Name, stored.Part, stored.Whole, stored.Floor, stored.Direction, stored.Passed);
    return new GoldenGateView(
        gate.Name, gate.Part, gate.Whole, gate.Floor, gate.Direction, gate.Passed,
        gate.ValueText, gate.FloorText);
}

static AssessmentListItem ToAssessmentListItem(Assessment a) => new(
    a.Id, a.ReviewId, a.Engine, a.Model, a.PromptVersion,
    a.Cost, a.LatencyMs, a.Findings?.Count ?? 0, a.CreatedAt);

/// <summary>Lightweight project row for the list view — with per-project counts.</summary>
record ProjectListItem(
    int Id, string Name, string Folder, DateTime CreatedAt, int ReviewCount, int AssessmentCount,
    DateTime? LastAssessmentAt);

/// <summary>Everything the project dashboard draws, aggregated server-side.</summary>
record ProjectStats(
    int ProjectId,
    int ReviewCount, int AssessmentCount, int EvaluationCount, int FindingCount,
    decimal TotalCost, long TotalInputTokens, long TotalOutputTokens,
    long LatencyP50Ms, long LatencyP95Ms, long LatencyP99Ms,
    IReadOnlyList<SliceCount> BySeverity,
    IReadOnlyList<SliceCount> ByCategory,
    IReadOnlyList<SliceCount> TopFiles,
    IReadOnlyList<AssessmentRunPoint> Runs,
    JudgeAverages? Judge);

record SliceCount(string Label, int Count);

record AssessmentRunPoint(
    int AssessmentId, DateTime CreatedAt, decimal Cost,
    int InputTokens, int OutputTokens, long LatencyMs, int FindingCount);

record JudgeAverages(
    double Correctness, double Actionability, double Calibration,
    double SignalToNoise, double Overall, int EvaluationCount);

/// <summary>Lightweight review row for the list view — omits the full <c>Content</c>.</summary>
record ReviewListItem(
    int Id, int ProjectId, string? ProjectName, string? Source, string ContentHash,
    DateTime CreatedAt, int AssessmentCount);

/// <summary>One golden run as a row: what it was, what it cost, how fast it was, and the verdict.</summary>
record GoldenRunListItem(
    int Id, string Model, string? Engine, string? Skills, string? PromptVersion,
    DateTime StartedAt, long DurationMs, decimal Cost, int InputTokens, int OutputTokens,
    long LatencyP50Ms, long LatencyP95Ms, long LatencyP99Ms,
    bool Approved, int CaseCount, int ApprovedCaseCount, int ReviewCount, string? ReportFile);

/// <summary>One golden run with everything it was judged on.</summary>
record GoldenRunDetail(
    GoldenRunListItem Run,
    IReadOnlyList<GoldenGateView> Gates,
    IReadOnlyList<GoldenCaseScore> Cases);

/// <summary>A gate as measured, plus the sentence the report prints for it.</summary>
record GoldenGateView(
    string Name, int Part, int Whole, int Floor, GateDirection Direction, bool Passed,
    string Value, string FloorText);

/// <summary>Lightweight assessment row for the list view — omits <c>Findings</c> and <c>Summary</c>.</summary>
record AssessmentListItem(
    int Id, int ReviewId, string? Engine, string? Model, string? PromptVersion,
    decimal Cost, long LatencyMs, int FindingCount, DateTime CreatedAt);
