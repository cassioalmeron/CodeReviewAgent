using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Infra;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden run end to end, over the bundled cases, with a fake LLM and a throwaway database:
/// no call is spent and nothing reaches the real store. What this covers that
/// <see cref="GoldenScorerTests"/> cannot is the wiring — that each case turns into a result of
/// the right kind, and that the diffs and assessments are persisted as the run goes.
/// </summary>
public class GoldenEvaluatorTests
{
    // One prompt version reproduces today's set: same round count, same results, no comparison.
    private static readonly List<string> SingleVersion = ["v3"];

    // Answers with the finding that case-01 plants: right file, a snippet that really is an
    // added line of its diff, and a keyword the case expects.
    private const string CaughtSqlInjection = """
        {
            "summary": "One issue found.",
            "findings": [
                {
                    "file": "src/Data/UserRepository.cs",
                    "code_snippet": "var sql = \"SELECT * FROM Users WHERE Name = '\" + name + \"'\";",
                    "severity": "critical",
                    "category": "security",
                    "problem": "SQL injection through string concatenation.",
                    "suggestion": "Use a parameterized query."
                }
            ]
        }
        """;

    [Fact]
    public void Run_ScoresEveryBundledCaseAndPersistsAsItGoes()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            // Off, so the fake is not also asked to choose skills — this test is about the loop.
            Environment.SetEnvironmentVariable("SKILLS", "off");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var projects = new EfProjectRepository(context);
            var reviews = new EfReviewRepository(context);
            var assessments = new EfAssessmentRepository(context);

            var run = GoldenEvaluator.Run(new FakeLlmClient(CaughtSqlInjection), projects, reviews, assessments, SingleVersion);

            var cases = GoldenEvaluator.LoadCases();
            Assert.Equal(cases.Count, run.Results.Count);
            Assert.All(run.Results, r => Assert.Equal(1, r.Runs));

            // The planted case the fake answers correctly is caught; every other detection case
            // is missed, which is what one fixed answer should produce.
            Assert.Equal(1, run.Results.Single(r => r.Name == "sql-injection").Successes);
            Assert.All(
                run.Results.Where(r => r.Kind == GoldenKind.Detection && r.Name != "sql-injection"),
                r => Assert.Equal(0, r.Successes));
            Assert.All(run.Results.Where(r => r.Successes == 0), r => Assert.False(string.IsNullOrWhiteSpace(r.MissDetail)));

            // The traps resist: the fake never cites their bait, so nothing falls for them. This
            // is the opposite verdict from the misses above, reached through the same loop.
            Assert.All(run.Results.Where(r => r.Kind == GoldenKind.Trap), r => Assert.Equal(r.Runs, r.Successes));

            // Each case's diff is stored once under the golden project, with one assessment per run.
            var golden = projects.List().Single(p => p.Folder == "golden");
            Assert.Equal(cases.Count, reviews.List().Count(r => r.ProjectId == golden.Id));
            Assert.Equal(cases.Count, assessments.List().Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    // Flags the bait of case-08 as invalid syntax — exactly the mistake the trap is built to
    // catch a model making.
    private const string FellForTheExtensionBlock = """
        {
            "summary": "One issue found.",
            "findings": [
                {
                    "file": "src/Domain/MoneyExtensions.cs",
                    "code_snippet": "extension(decimal value)",
                    "severity": "critical",
                    "category": "bug",
                    "problem": "Invalid syntax: 'extension' is not a C# keyword.",
                    "suggestion": "Use a static method with a 'this' parameter."
                }
            ]
        }
        """;

    /// <summary>
    /// The other half of the verdict: a model that flags correct code loses the trap. Without
    /// this, every trap in the set could be passing because nothing ever reaches its bait.
    /// </summary>
    [Fact]
    public void Run_CountsATrapAsLostWhenTheModelFlagsTheBait()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            Environment.SetEnvironmentVariable("SKILLS", "off");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var run = GoldenEvaluator.Run(
                new FakeLlmClient(FellForTheExtensionBlock),
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                new EfAssessmentRepository(context),
                SingleVersion);

            var trap = run.Results.Single(r => r.Name == "extension-block");
            Assert.Equal(0, trap.Successes);
            Assert.Contains("extension(decimal value)", trap.MissDetail);

            // The other traps are untouched by this answer: falling for one is not falling for all.
            Assert.All(
                run.Results.Where(r => r.Kind == GoldenKind.Trap && r.Name != "extension-block"),
                r => Assert.Equal(r.Runs, r.Successes));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>
    /// Tuning a skill against one stubborn case should not cost a full pass over fifteen.
    /// </summary>
    [Theory]
    [InlineData("extension-block", 1)]
    [InlineData("extension-block,raw-string-literal", 2)]
    [InlineData("EXTENSION-BLOCK", 1)]
    public void Run_WithAFilter_ScoresOnlyTheNamedCases(string filter, int expected)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            Environment.SetEnvironmentVariable("SKILLS", "off");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var run = GoldenEvaluator.Run(
                new FakeLlmClient(CaughtSqlInjection),
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                new EfAssessmentRepository(context),
                SingleVersion,
                filter);

            Assert.Equal(expected, run.Results.Count);
            Assert.All(run.Results, r => Assert.Contains(r.Name, filter, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>
    /// The runs go out concurrently, but the report must not: results stay in case order, and
    /// each run still persists exactly one assessment. Persistence is the fragile part —
    /// <c>DbContext</c> is not thread-safe — so it has to stay off the parallel path.
    /// </summary>
    [Fact]
    public void Run_InParallel_KeepsCaseOrderAndPersistsEveryRunExactlyOnce()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        var previousParallel = Environment.GetEnvironmentVariable("GOLDEN_PARALLELISM");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "3");
            Environment.SetEnvironmentVariable("SKILLS", "off");
            Environment.SetEnvironmentVariable("GOLDEN_PARALLELISM", "8");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();
            var assessments = new EfAssessmentRepository(context);

            var run = GoldenEvaluator.Run(
                new FakeLlmClient(CaughtSqlInjection),
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                assessments,
                SingleVersion);

            var cases = GoldenEvaluator.LoadCases();
            Assert.Equal(cases.Select(c => c.Name), run.Results.Select(r => r.Name));
            Assert.Equal(cases.Count * 3, assessments.List().Count);
            Assert.All(run.Results, r => Assert.Equal(3, r.Runs));

            // The verdicts survive the concurrency: same answers as the sequential run.
            Assert.Equal(3, run.Results.Single(r => r.Name == "sql-injection").Successes);
            Assert.All(run.Results.Where(r => r.Kind == GoldenKind.Trap), r => Assert.Equal(r.Runs, r.Successes));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            Environment.SetEnvironmentVariable("GOLDEN_PARALLELISM", previousParallel);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>
    /// Running the set must not touch the file system. Publishing the result is the caller's
    /// job, so a test suite can exercise the whole flow without littering the build output —
    /// the same split the pipeline already uses for <c>Review()</c> and persistence.
    /// </summary>
    [Fact]
    public void Run_WritesNothingToDisk()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            Environment.SetEnvironmentVariable("SKILLS", "off");

            var reports = Path.Combine(AppContext.BaseDirectory, "reports");
            var reviews = Path.Combine(AppContext.BaseDirectory, "reviews");
            var before = (Count(reports), Count(reviews));

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            GoldenEvaluator.Run(
                new FakeLlmClient(CaughtSqlInjection),
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                new EfAssessmentRepository(context),
                SingleVersion);

            Assert.Equal(before, (Count(reports), Count(reviews)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    private static int Count(string directory) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory).Length : 0;

    /// <summary>A filter that matches nothing must say so, not quietly score an empty set.</summary>
    [Fact]
    public void Run_WithAnUnknownCase_Throws()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var error = Assert.Throws<InvalidOperationException>(() => GoldenEvaluator.Run(
                new FakeLlmClient(CaughtSqlInjection),
                new EfProjectRepository(context),
                new EfReviewRepository(context),
                new EfAssessmentRepository(context),
                SingleVersion,
                "no-such-case"));

            Assert.Contains("no-such-case", error.Message);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>
    /// The same diff must not be stored twice across runs: the golden set relies on
    /// <c>GetOrAdd</c> reusing by content hash, so a case accumulates assessments, not reviews.
    /// </summary>
    [Fact]
    public void Run_ReusesTheStoredDiffsOnASecondRun()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            Environment.SetEnvironmentVariable("SKILLS", "off");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var projects = new EfProjectRepository(context);
            var reviews = new EfReviewRepository(context);
            var assessments = new EfAssessmentRepository(context);
            var client = new FakeLlmClient(CaughtSqlInjection);

            GoldenEvaluator.Run(client, projects, reviews, assessments, SingleVersion);
            GoldenEvaluator.Run(client, projects, reviews, assessments, SingleVersion);

            var cases = GoldenEvaluator.LoadCases();
            Assert.Equal(cases.Count, reviews.List().Count);          // diffs reused
            Assert.Equal(cases.Count * 2, assessments.List().Count);  // one assessment per run
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>
    /// T1 of US-011: with two prompt versions, every case yields one result per side — the
    /// pair the future pairwise judge needs — and the sides never share a review id, only the
    /// diff underneath them.
    /// </summary>
    [Fact]
    public void Run_WithTwoPromptVersions_YieldsOneResultPerCasePerSide()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-golden-{Guid.NewGuid():N}.db");
        var previousRuns = Environment.GetEnvironmentVariable("GOLDEN_RUNS");
        var previousSkills = Environment.GetEnvironmentVariable("SKILLS");
        try
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", "1");
            Environment.SetEnvironmentVariable("SKILLS", "off");

            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.EnsureCreated();

            var reviews = new EfReviewRepository(context);
            var assessments = new EfAssessmentRepository(context);

            var run = GoldenEvaluator.Run(
                new FakeLlmClient(CaughtSqlInjection),
                new EfProjectRepository(context),
                reviews,
                assessments,
                ["v3", "v1"],
                "sql-injection");

            Assert.Equal(2, run.Results.Count);
            Assert.Equal(["v3", "v1"], run.Results.Select(r => r.PromptVersion));
            Assert.All(run.Results, r => Assert.Equal(1, r.Successes));

            // Same diff, reused across both sides; one assessment per side.
            Assert.Single(reviews.List());
            Assert.Equal(2, assessments.List().Count);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }
}
