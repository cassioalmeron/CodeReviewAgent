using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class EfRepositoryTests
    {
        private static ReviewDbContext NewContext(string dbPath)
        {
            var context = new ReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();
            return context;
        }

        [Fact]
        public void GetOrAdd_ReusesSameContentAndInsertsNewContent()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
            try
            {
                using var context = NewContext(dbPath);
                var diffs = new EfDiffRepository(context);

                var first = diffs.GetOrAdd(new Diff { Content = "same content", Source = "golden:a" });
                var again = diffs.GetOrAdd(new Diff { Content = "same content", Source = "golden:a" });
                var other = diffs.GetOrAdd(new Diff { Content = "different content", Source = "golden:b" });

                Assert.Equal(first, again);      // same content → reused
                Assert.NotEqual(first, other);   // different content → new diff
            }
            finally
            {
                try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
            }
        }

        [Fact]
        public void SaveAndGet_RoundTripsDiffAndAnalysisWithFindings()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
            try
            {
                int diffId, analysisId;

                // Write with one context...
                using (var context = NewContext(dbPath))
                {
                    var diffs = new EfDiffRepository(context);
                    var analyses = new EfAnalysisRepository(context);

                    diffId = diffs.Save(new Diff
                    {
                        Content = "diff --git a/a.cs b/a.cs",
                        Source = "local",
                        CreatedAt = DateTime.UtcNow,
                    });
                    Assert.Equal(1, diffId);

                    analysisId = analyses.Save(new Analysis
                    {
                        DiffId = diffId,
                        Summary = "sum",
                        Findings = [new Finding("a.cs", "+ bad", Severity.Warning, Category.Bug, "p", "s", 3)],
                        Engine = "openai",
                        Model = "x",
                        PromptVersion = "v3",
                        Cost = 0.5m,
                        LatencyMs = 12,
                        InputTokens = 4,
                        OutputTokens = 2,
                        CreatedAt = DateTime.UtcNow,
                    });
                    Assert.Equal(1, analysisId);

                    // Identity keeps incrementing.
                    Assert.Equal(2, diffs.Save(new Diff { Content = "x", CreatedAt = DateTime.UtcNow }));
                }

                int evaluationId;
                using (var context = NewContext(dbPath))
                {
                    evaluationId = new EfJudgeEvaluationRepository(context).Save(new JudgeEvaluation
                    {
                        AnalysisId = analysisId,
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

                // ...and read back with a fresh one, so the findings JSON conversion is
                // exercised on both write and read (not served from the change tracker).
                using (var context = NewContext(dbPath))
                {
                    var loadedDiff = new EfDiffRepository(context).Get(diffId);
                    Assert.NotNull(loadedDiff);
                    Assert.Equal(Hashing.Sha256("diff --git a/a.cs b/a.cs"), loadedDiff!.ContentHash);

                    var loaded = new EfAnalysisRepository(context).Get(analysisId);

                    Assert.NotNull(loaded);
                    Assert.Equal(diffId, loaded!.DiffId);
                    Assert.Equal("sum", loaded.Summary);
                    var finding = Assert.Single(loaded.Findings!);
                    Assert.Equal("a.cs", finding.File);
                    Assert.Equal(Severity.Warning, finding.Severity);
                    Assert.Equal(Category.Bug, finding.Category);
                    Assert.Equal(3, finding.Line);

                    var evaluation = new EfJudgeEvaluationRepository(context).Get(evaluationId);
                    Assert.NotNull(evaluation);
                    Assert.Equal(analysisId, evaluation!.AnalysisId);
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
