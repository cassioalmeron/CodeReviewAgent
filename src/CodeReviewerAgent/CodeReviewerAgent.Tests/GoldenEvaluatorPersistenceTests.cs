using Microsoft.EntityFrameworkCore;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Infra;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The two overloads of <c>Run</c>: one that evaluates and one that also persists. What is worth
/// pinning is the seam between them — that the evaluation needs no repository at all, and that
/// the persisting one keeps the rounds already paid for when the run dies part-way, which is the
/// failure this project has actually lived through.
/// </summary>
public class GoldenEvaluatorPersistenceTests
{
    private const string EmptyReview = "{\"summary\":\"nothing found\",\"findings\":[]}";

    // One round at a time, so "the client refused after three" means exactly three came back, and
    // skills off so the fake is not also asked to choose them. These were GOLDEN_RUNS,
    // GOLDEN_PARALLELISM and SKILLS in the process environment, set and restored around each test
    // by a disposable; the evaluator takes them as an argument now (US-018).
    private static readonly GoldenSettings Sequential = new(Runs: 1, Parallelism: 1, Skills: "off");

    [Fact]
    public void Run_WithoutRepositories_EvaluatesTheWholeSet()
    {
        var run = GoldenEvaluator.Run(
            new FakeLlmClient(EmptyReview), ["v3"], settings: Sequential).Scored();

        Assert.Equal(GoldenEvaluator.LoadCases().Count, run.Results.Count);
        Assert.All(run.Results, r => Assert.Equal(1, r.Runs));
    }

    /// <summary>
    /// A quota exhausted on round 55 of 60 lost the 54 already bought, once, for real money. The
    /// rounds that came back are written; the exception still propagates, because partial work is
    /// worth keeping and a rate computed over a partial run is not.
    /// </summary>
    [Fact]
    public void Run_WhenThePaidPhaseDies_StillPersistsWhatWasAlreadyBought()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-persist-{Guid.NewGuid():N}.db");

        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.Migrate();

            var client = new FailsAfterLlmClient(succeedFor: 3, EmptyReview);

            var result = GoldenEvaluator.Run(
                client, TestRepositories.For(context), ["v3"], settings: Sequential);

            // The failure is a value, not an exception: that is what lets the caller persist and
            // report what came back instead of unwinding past it.
            Assert.False(result.Succeeded);
            Assert.NotNull(result.Failure);

            // Nothing is scored, because a rate over a partial set would be a wrong number.
            Assert.Null(result.Score);

            // Three rounds came back before the client started refusing, and all three are on disk.
            Assert.Equal(3, result.Completed.Count);
            Assert.Equal(3, context.Assessments.Count());

            // But no golden run: there is nothing scored to record.
            Assert.Empty(context.GoldenRuns);
            Assert.All(context.Assessments, a => Assert.Null(a.RunId));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    [Fact]
    public void Persist_AScoredWholeRun_RecordsTheRunWithItsCasesAndGates_AndLinksEveryAssessment()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-persist-{Guid.NewGuid():N}.db");

        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.Migrate();
            var before = DateTime.UtcNow;

            var result = GoldenEvaluator.Run(new FakeLlmClient(EmptyReview), ["v3"], settings: Sequential);
            GoldenEvaluator.Persist(result, TestRepositories.For(context), "configured-model", "reports/report.md");

            var run = new EfGoldenRunRepository(context).List().Single();
            var caseCount = GoldenEvaluator.LoadCases().Count;

            Assert.Equal("configured-model", run.Model);
            Assert.Equal("off", run.Skills);
            Assert.Equal("v3", run.PromptVersion);
            Assert.Equal("reports/report.md", run.ReportFile);
            Assert.InRange(run.StartedAt, before.AddSeconds(-1), DateTime.UtcNow);
            Assert.True(run.DurationMs >= 0);
            Assert.Equal(caseCount, run.Cases!.Count);
            Assert.Equal(5, run.Gates!.Count);

            // An empty review catches nothing, so the model cannot be approved.
            Assert.False(run.Approved);

            Assert.Equal(caseCount, context.Assessments.Count());
            Assert.All(context.Assessments, a => Assert.Equal(run.Id, a.RunId));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    [Fact]
    public void Run_WithAFilter_KeepsTheAssessmentsButRecordsNoRun()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-persist-{Guid.NewGuid():N}.db");

        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.Migrate();

            var firstCase = GoldenEvaluator.LoadCases()[0].Name;
            var result = GoldenEvaluator.Run(
                new FakeLlmClient(EmptyReview), TestRepositories.For(context), ["v3"], filter: firstCase,
                settings: Sequential);

            Assert.NotNull(result.Score);
            Assert.False(result.WholeSet);
            Assert.Empty(context.GoldenRuns);
            Assert.Equal(1, context.Assessments.Count());
            Assert.All(context.Assessments, a => Assert.Null(a.RunId));
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    /// <summary>Answers a fixed number of times, then behaves like an exhausted quota.</summary>
    private sealed class FailsAfterLlmClient(int succeedFor, string responseText) : ILlmClient
    {
        private int _calls;

        public MessageResponse Request(object requestBody)
        {
            if (Interlocked.Increment(ref _calls) > succeedFor)
                throw new InvalidOperationException("credit balance is too low");

            return new MessageResponse
            {
                Model = "fake-model",
                Content = [new ContentBlock { Type = "text", Text = responseText }],
                Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
            };
        }
    }
}
