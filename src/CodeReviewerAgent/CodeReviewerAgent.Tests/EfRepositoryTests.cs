using Microsoft.EntityFrameworkCore;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class EfRepositoryTests
{
    private static CodeReviewDbContext NewContext(string dbPath)
    {
        var context = new CodeReviewDbContext(
            o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public void Project_GetOrAdd_IsIdempotentByFolder()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            using var context = NewContext(dbPath);
            var projects = new EfProjectRepository(context);

            var first = projects.GetOrAdd("/repos/app", "app");
            var again = projects.GetOrAdd("/repos/app", "renamed-would-be-ignored");
            var other = projects.GetOrAdd("/repos/other", "other");

            Assert.Equal(first.Id, again.Id);       // same folder → reused (name not overwritten)
            Assert.Equal("app", again.Name);
            Assert.NotEqual(first.Id, other.Id);    // different folder → new project

            // Windows paths are case-insensitive: a different-cased drive must reuse, not duplicate.
            var casing = projects.GetOrAdd(@"/REPOS/App", "app");
            Assert.Equal(first.Id, casing.Id);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    [Fact]
    public void Review_GetOrAdd_ReusesWithinProjectButNotAcrossProjects()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            using var context = NewContext(dbPath);
            var projects = new EfProjectRepository(context);
            var reviews = new EfReviewRepository(context);

            var a = projects.GetOrAdd("/repos/a", "a");
            var b = projects.GetOrAdd("/repos/b", "b");

            var first = reviews.GetOrAdd(new Review { ProjectId = a.Id, Content = "same content" });
            var again = reviews.GetOrAdd(new Review { ProjectId = a.Id, Content = "same content" });
            var crossProject = reviews.GetOrAdd(new Review { ProjectId = b.Id, Content = "same content" });

            Assert.Equal(first, again);            // same project + content → reused
            Assert.NotEqual(first, crossProject);  // same content, other project → new review
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    [Fact]
    public void SaveAndGet_RoundTripsReviewAndAssessmentWithFindings()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            int projectId, reviewId, assessmentId;

            // Write with one context...
            using (var context = NewContext(dbPath))
            {
                var projects = new EfProjectRepository(context);
                var reviews = new EfReviewRepository(context);
                var assessments = new EfAssessmentRepository(context);

                projectId = projects.GetOrAdd("/repos/a", "a").Id;

                reviewId = reviews.Save(new Review
                {
                    ProjectId = projectId,
                    Content = "diff --git a/a.cs b/a.cs",
                    Source = "local",
                    CreatedAt = DateTime.UtcNow,
                });
                Assert.Equal(1, reviewId);

                assessmentId = assessments.Save(new Assessment
                {
                    ReviewId = reviewId,
                    Summary = "sum",
                    Findings = [new Finding("a.cs", "+ bad", Severity.Warning, Category.Bug, "p", "s", 3)],
                    Engine = "openai",
                    Model = "x",
                    PromptVersion = "v3",
                    Skills = "csharp,react",
                    Cost = 0.5m,
                    LatencyMs = 12,
                    InputTokens = 4,
                    OutputTokens = 2,
                    CreatedAt = DateTime.UtcNow,
                });
                Assert.Equal(1, assessmentId);

                // Identity keeps incrementing.
                Assert.Equal(2, reviews.Save(new Review { ProjectId = projectId, Content = "x", CreatedAt = DateTime.UtcNow }));
            }

            int evaluationId;
            using (var context = NewContext(dbPath))
            {
                evaluationId = new EfEvaluationRepository(context).Save(new Evaluation
                {
                    AssessmentId = assessmentId,
                    RubricVersion = "v1",
                    JudgeModel = "claude-sonnet-4-6",
                    Correctness = 5,
                    Actionability = 4,
                    Calibration = 3,
                    SignalToNoise = 4,
                    Overall = 4,
                    Rationale = "solid",
                    Cost = 0.01m,
                    LatencyMs = 20,
                    InputTokens = 30,
                    OutputTokens = 10,
                    CreatedAt = DateTime.UtcNow,
                });
                Assert.Equal(1, evaluationId);
            }

            // ...and read back with a fresh one, so the findings child table is exercised on
            // both write and read (not served from the change tracker).
            using (var context = NewContext(dbPath))
            {
                var loadedReview = new EfReviewRepository(context).Get(reviewId);
                Assert.NotNull(loadedReview);
                Assert.Equal(projectId, loadedReview!.ProjectId);
                Assert.Equal(Hashing.Sha256("diff --git a/a.cs b/a.cs"), loadedReview.ContentHash);

                var loaded = new EfAssessmentRepository(context).Get(assessmentId);

                Assert.NotNull(loaded);
                Assert.Equal(reviewId, loaded!.ReviewId);
                Assert.Equal("sum", loaded.Summary);
                Assert.Equal("csharp,react", loaded.Skills);
                var finding = Assert.Single(loaded.Findings!);
                Assert.Equal("a.cs", finding.File);
                Assert.Equal(Severity.Warning, finding.Severity);
                Assert.Equal(Category.Bug, finding.Category);
                Assert.Equal(3, finding.Line);
                Assert.Equal(assessmentId, finding.AssessmentId);

                var evaluation = new EfEvaluationRepository(context).Get(evaluationId);
                Assert.NotNull(evaluation);
                Assert.Equal(assessmentId, evaluation!.AssessmentId);
                Assert.Equal(4, evaluation.Overall);
                Assert.Equal("solid", evaluation.Rationale);
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file; leak the temp file */ }
        }
    }

    [Fact]
    public void GoldenRun_RoundTripsWithChildren_AndDeletingItRemovesEverythingButTheDiff()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            int runId, reviewId;

            using (var context = NewContext(dbPath))
            {
                var project = new EfProjectRepository(context).GetOrAdd("golden", "Golden Set");
                reviewId = new EfReviewRepository(context).Save(new Review
                {
                    ProjectId = project.Id,
                    Content = "diff --git a/a.cs b/a.cs",
                    CreatedAt = DateTime.UtcNow,
                });

                var run = new GoldenRun
                {
                    Model = "deepseek/deepseek-v4-flash-0731",
                    Engine = "openrouter",
                    Skills = "off",
                    PromptVersion = "v3",
                    StartedAt = new DateTime(2026, 9, 11, 21, 49, 52, DateTimeKind.Utc),
                    DurationMs = 2_957_000,
                    Cost = 0.09660000m,
                    InputTokens = 50_216,
                    OutputTokens = 324_112,
                    LatencyP50Ms = 59_871,
                    LatencyP95Ms = 508_599,
                    LatencyP99Ms = 1_082_010,
                    Approved = true,
                    Cases = [.. Enumerable.Range(1, 15).Select(i => new GoldenCaseScore
                    {
                        CaseName = $"case-{i}",
                        Kind = i <= 12 ? GoldenKind.Detection : GoldenKind.Trap,
                        Runs = 5,
                        CleanRounds = 4,
                        Approved = true,
                    })],
                    Gates = [.. new[] { "Detection", "Trap resistance", "Precision", "Exact calibration", "Trap noise" }
                        .Select(name => new GoldenGate { Name = name, Part = 41, Whole = 60, Floor = 60, Passed = true })],
                };
                context.GoldenRuns.Add(run);
                context.SaveChanges();
                runId = run.Id;

                var assessments = new EfAssessmentRepository(context);
                for (var i = 0; i < 3; i++)
                    assessments.Save(new Assessment
                    {
                        ReviewId = reviewId,
                        RunId = runId,
                        Cost = 0.00006774m,
                        DiscardedFindings = 1,
                        Findings = [new Finding("a.cs", "+ bad", Severity.Critical, Category.Security, "p", "s", 1)],
                        CreatedAt = DateTime.UtcNow,
                    });
            }

            using (var context = NewContext(dbPath))
            {
                var loaded = context.GoldenRuns
                    .Include(r => r.Cases)
                    .Include(r => r.Gates)
                    .Single(r => r.Id == runId);

                Assert.Equal(15, loaded.Cases!.Count);
                Assert.Equal(5, loaded.Gates!.Count);
                Assert.Equal(0.09660000m, loaded.Cost);
                Assert.Equal(DateTimeKind.Utc, loaded.StartedAt.Kind);
                var assessment = context.Assessments.Include(a => a.Findings).First(a => a.RunId == runId);
                Assert.Equal(0.00006774m, assessment.Cost);
                Assert.Equal(1, assessment.DiscardedFindings);

                // Delete in the database, not in the change tracker, so the cascade under test is the
                // schema's and not EF's.
                context.GoldenRuns.Where(r => r.Id == runId).ExecuteDelete();

                Assert.Equal(0, context.GoldenCaseScores.Count(c => c.RunId == runId));
                Assert.Equal(0, context.GoldenGates.Count(g => g.RunId == runId));
                Assert.Equal(0, context.Assessments.Count(a => a.RunId == runId));
                Assert.Equal(0, context.Set<Finding>().Count());
                Assert.NotNull(context.Reviews.Find(reviewId));
            }
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file; leak the temp file */ }
        }
    }
}
