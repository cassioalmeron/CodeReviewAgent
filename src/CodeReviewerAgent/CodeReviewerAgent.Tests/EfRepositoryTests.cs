using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class EfRepositoryTests
    {
        private static CodeReviewDbContext NewContext(string dbPath)
        {
            var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();
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
    }
}
