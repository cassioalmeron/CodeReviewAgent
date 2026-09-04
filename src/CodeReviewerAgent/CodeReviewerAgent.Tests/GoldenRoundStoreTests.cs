using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The golden set's durable record of paid rounds. What matters here is not that the file
/// round-trips — it is that a stored round is only ever reused by the configuration that
/// produced it, because a round silently borrowed from another model is a wrong result that
/// looks exactly like a right one.
/// </summary>
public class GoldenRoundStoreTests
{
    private static ReviewResult Review(string summary) =>
        new(summary, [], "claude", "haiku", "v3", 0.01m, 100, 10, 20);

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"cra-rounds-{Guid.NewGuid():N}.jsonl");

    [Fact]
    public void Record_MakesTheRoundAvailableToTheNextRun()
    {
        var path = TempPath();
        try
        {
            new FileGoldenRoundStore(path, "haiku", "off")
                .Record("sql-injection", "v3", 0, Review("found it"));

            var resumed = new FileGoldenRoundStore(path, "haiku", "off");

            Assert.Equal(1, resumed.ResumableCount);
            Assert.Equal("found it", resumed.Find("sql-injection", "v3", 0)?.Summary);
            Assert.Null(resumed.Find("sql-injection", "v3", 1));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Find_IgnoresRoundsFromAnotherModel()
    {
        var path = TempPath();
        try
        {
            new FileGoldenRoundStore(path, "haiku", "off")
                .Record("sql-injection", "v3", 0, Review("found it"));

            var otherModel = new FileGoldenRoundStore(path, "sonnet", "off");

            Assert.Equal(0, otherModel.ResumableCount);
            Assert.Null(otherModel.Find("sql-injection", "v3", 0));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Find_IgnoresRoundsFromAnotherSkillsCondition()
    {
        var path = TempPath();
        try
        {
            new FileGoldenRoundStore(path, "haiku", "off")
                .Record("sql-injection", "v3", 0, Review("found it"));

            Assert.Null(new FileGoldenRoundStore(path, "haiku", "all").Find("sql-injection", "v3", 0));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// A run that cannot name its own model cannot claim a stored round was produced by it. It
    /// still records, so the work is not lost; it just refuses to resume.
    /// </summary>
    [Fact]
    public void UnknownModel_RecordsButNeverResumes()
    {
        var path = TempPath();
        try
        {
            var store = new FileGoldenRoundStore(path, model: null, skills: "off");
            store.Record("sql-injection", "v3", 0, Review("found it"));

            Assert.Equal(0, store.ResumableCount);
            Assert.Null(store.Find("sql-injection", "v3", 0));
            Assert.Single(FileGoldenRoundStore.Load(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The whole point of JSON Lines: the run that died mid-write is the one being resumed, so
    /// a truncated last line must cost that line and nothing else.
    /// </summary>
    [Fact]
    public void Load_KeepsEverythingBeforeALineACrashTruncated()
    {
        var path = TempPath();
        try
        {
            var store = new FileGoldenRoundStore(path, "haiku", "off");
            store.Record("sql-injection", "v3", 0, Review("first"));
            store.Record("hardcoded-secret", "v3", 0, Review("second"));
            File.AppendAllText(path, "{\"case\":\"off-by-one\",\"promptVer");

            Assert.Equal(2, FileGoldenRoundStore.Load(path).Count);
            Assert.Equal(2, new FileGoldenRoundStore(path, "haiku", "off").ResumableCount);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The end that matters: a second run over the same configuration spends nothing, because
    /// every round it needs is already on disk.
    /// </summary>
    [Fact]
    public void SecondRun_ReusesEveryRoundAndCallsTheModelNoFurtherTimes()
    {
        var path = TempPath();
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

            const string model = "haiku";
            var client = new CountingLlmClient(model);
            var caseCount = GoldenEvaluator.LoadCases().Count;

            RunOnce(context, client, new FileGoldenRoundStore(path, model, "off"));
            var afterFirst = client.Calls;

            var second = new FileGoldenRoundStore(path, model, "off");
            Assert.Equal(caseCount, second.ResumableCount);

            RunOnce(context, client, second);

            Assert.Equal(caseCount, afterFirst);
            Assert.Equal(afterFirst, client.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GOLDEN_RUNS", previousRuns);
            Environment.SetEnvironmentVariable("SKILLS", previousSkills);
            File.Delete(path);
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    private static void RunOnce(CodeReviewDbContext context, ILlmClient client, IGoldenRoundStore store) =>
        GoldenEvaluator.Run(
            client,
            new EfProjectRepository(context),
            new EfReviewRepository(context),
            new EfAssessmentRepository(context),
            ["v3"],
            filter: null,
            store: store);

    /// <summary>Answers anything, and counts how many times it was asked.</summary>
    private sealed class CountingLlmClient(string? model) : ILlmClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public MessageResponse Request(object requestBody)
        {
            Interlocked.Increment(ref _calls);
            return new MessageResponse
            {
                Model = model,
                Content = [new ContentBlock { Type = "text", Text = "{\"summary\":\"none\",\"findings\":[]}" }],
                Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
            };
        }
    }
}
