using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using CodeReviewerAgent.Infra;
using DotNetEnv;

// Uses DotNetEnv (NoClobber) so STORAGE / DB_CONNECTION resolve exactly as they do for the CLI.
Env.NoClobber().Load(Path.Combine(AppContext.BaseDirectory, ".env"));

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

// Read-only store, shared for the process lifetime — selected by STORAGE (files / sqlite / postgres).
var store = RepositoryFactory.Create();
builder.Services.AddSingleton(store);

var app = builder.Build();
app.UseCors(DevCors);

app.UseSwagger();
app.UseSwaggerUI();

var projects = app.MapGroup("/api/projects");

projects.MapGet("/", () =>
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

projects.MapGet("/{id:int}", (int id) =>
    store.Projects.Get(id) is { } project ? Results.Ok(project) : Results.NotFound());

projects.MapGet("/{id:int}/reviews", (int id) =>
    store.Reviews.List().Where(r => r.ProjectId == id).Select(ToReviewListItem));

// Everything the project dashboard draws, in one aggregated payload (computed in-memory over List()).
projects.MapGet("/{id:int}/stats", (int id) =>
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

    return Results.Ok(new ProjectStats(
        id,
        reviewIds.Count, assessments.Count, evaluations.Count, findings.Count,
        assessments.Sum(a => a.Cost), assessments.Sum(a => (long)a.InputTokens),
        assessments.Sum(a => (long)a.OutputTokens),
        assessments.Count == 0 ? 0 : assessments.Average(a => a.LatencyMs),
        bySeverity, byCategory, topFiles, runs, judge));
});

var reviews = app.MapGroup("/api/reviews");

// `?projectId=N` filters to one project; without it, every review.
reviews.MapGet("/", (int? projectId) => store.Reviews.List()
    .Where(r => projectId is null || r.ProjectId == projectId)
    .Select(ToReviewListItem));

reviews.MapGet("/{id:int}", (int id) =>
    store.Reviews.Get(id) is { } review ? Results.Ok(review) : Results.NotFound());

reviews.MapGet("/{id:int}/assessments", (int id) =>
    store.Assessments.List().Where(a => a.ReviewId == id).Select(ToAssessmentListItem));

var assessmentsGroup = app.MapGroup("/api/assessments");

assessmentsGroup.MapGet("/", () => store.Assessments.List().Select(ToAssessmentListItem));

assessmentsGroup.MapGet("/{id:int}", (int id) =>
    store.Assessments.Get(id) is { } assessment ? Results.Ok(assessment) : Results.NotFound());

assessmentsGroup.MapGet("/{id:int}/evaluations", (int id) =>
    store.Evaluations.List().Where(e => e.AssessmentId == id));

var evaluations = app.MapGroup("/api/evaluations");

evaluations.MapGet("/", () => store.Evaluations.List());

evaluations.MapGet("/{id:int}", (int id) =>
    store.Evaluations.Get(id) is { } evaluation ? Results.Ok(evaluation) : Results.NotFound());

app.Run();

ReviewListItem ToReviewListItem(Review r) => new(
    r.Id, r.ProjectId, store.Projects.Get(r.ProjectId)?.Name, r.Source, r.ContentHash, r.CreatedAt,
    store.Assessments.List().Count(a => a.ReviewId == r.Id));

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
    decimal TotalCost, long TotalInputTokens, long TotalOutputTokens, double AvgLatencyMs,
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

/// <summary>Lightweight assessment row for the list view — omits <c>Findings</c> and <c>Summary</c>.</summary>
record AssessmentListItem(
    int Id, int ReviewId, string? Engine, string? Model, string? PromptVersion,
    decimal Cost, long LatencyMs, int FindingCount, DateTime CreatedAt);
