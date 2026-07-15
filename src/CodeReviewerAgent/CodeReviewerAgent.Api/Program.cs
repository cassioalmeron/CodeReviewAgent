using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Infra;

// Reuse the Core .env loader so STORAGE / DB_CONNECTION resolve exactly as they do for the CLI.
EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

var builder = WebApplication.CreateBuilder(args);

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

var diffs = app.MapGroup("/api/diffs");

diffs.MapGet("/", () =>
{
    var analyses = store.Analyses.List();
    return store.Diffs.List().Select(d => new DiffListItem(
        d.Id, d.Source, d.ContentHash, d.CreatedAt,
        analyses.Count(a => a.DiffId == d.Id)));
});

diffs.MapGet("/{id:int}", (int id) =>
    store.Diffs.Get(id) is { } diff ? Results.Ok(diff) : Results.NotFound());

diffs.MapGet("/{id:int}/analyses", (int id) =>
    store.Analyses.List().Where(a => a.DiffId == id).Select(ToAnalysisListItem));

var analyses = app.MapGroup("/api/analyses");

analyses.MapGet("/", () => store.Analyses.List().Select(ToAnalysisListItem));

analyses.MapGet("/{id:int}", (int id) =>
    store.Analyses.Get(id) is { } analysis ? Results.Ok(analysis) : Results.NotFound());

analyses.MapGet("/{id:int}/evaluations", (int id) =>
    store.JudgeEvaluations.List().Where(e => e.AnalysisId == id));

var evaluations = app.MapGroup("/api/evaluations");

evaluations.MapGet("/", () => store.JudgeEvaluations.List());

evaluations.MapGet("/{id:int}", (int id) =>
    store.JudgeEvaluations.Get(id) is { } evaluation ? Results.Ok(evaluation) : Results.NotFound());

app.Run();

static AnalysisListItem ToAnalysisListItem(Analysis a) => new(
    a.Id, a.DiffId, a.Engine, a.Model, a.PromptVersion,
    a.Cost, a.LatencyMs, a.Findings?.Count ?? 0, a.CreatedAt);

/// <summary>Lightweight diff row for the list view — omits the full <c>Content</c>.</summary>
record DiffListItem(int Id, string? Source, string ContentHash, DateTime CreatedAt, int AnalysisCount);

/// <summary>Lightweight analysis row for the list view — omits <c>Findings</c> and <c>Summary</c>.</summary>
record AnalysisListItem(
    int Id, int DiffId, string? Engine, string? Model, string? PromptVersion,
    decimal Cost, long LatencyMs, int FindingCount, DateTime CreatedAt);
