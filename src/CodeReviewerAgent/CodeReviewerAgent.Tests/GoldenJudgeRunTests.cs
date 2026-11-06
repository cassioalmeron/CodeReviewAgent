using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Golden;
using CodeReviewerAgent.Core.Judge;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Infra;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class GoldenJudgeRunTests
{
    private const string AllFive =
        """{"correctness":5,"actionability":5,"calibration":5,"signal_to_noise":5,"overall":5,"rationale":"x"}""";

    private const string Model = "claude-sonnet-4-6";

    // Answers every request with one fixed single-call judgment and counts the requests.
    private sealed class CountingLlmClient : ILlmClient
    {
        private int _requests;

        public int Requests => _requests;

        public MessageResponse Request(object requestBody)
        {
            Interlocked.Increment(ref _requests);
            var body = (Dictionary<string, object>)requestBody;
            var system = (string)body["system"];
            // A sectioned slice asks for one criterion; the single rubric asks for all five numbers.
            var text = system.Contains("Score one criterion only") ? """{"score":5,"rationale":"r"}""" : AllFive;
            return new MessageResponse
            {
                Model = "fake-model",
                Content = [new ContentBlock { Type = "text", Text = text }],
                Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
            };
        }
    }

    // A migrated SQLite store holding one golden run with the given number of assessments.
    private static void WithRun(int assessmentCount, Action<RepositoryContext, int> test) =>
        WithRun(assessmentCount, 1, test);

    // Each review gets its own diff, and each review is assessed assessmentsPerReview times.
    private static void WithRun(int reviewCount, int assessmentsPerReview, Action<RepositoryContext, int> test)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cra-test-{Guid.NewGuid():N}.db");
        try
        {
            using var context = new CodeReviewDbContext(
                o => new SqliteProviderStrategy().Configure(o, $"Data Source={dbPath}"));
            context.Database.Migrate();
            var repositories = TestRepositories.For(context);

            var runId = repositories.GoldenRuns.Save(new GoldenRun { Model = "m", StartedAt = DateTime.UtcNow });
            var project = repositories.Projects.GetOrAdd("/repos/golden", "golden");
            for (var i = 0; i < reviewCount; i++)
            {
                var reviewId = repositories.Reviews.GetOrAdd(new Review { ProjectId = project.Id, Content = $"diff {i}" });
                for (var round = 0; round < assessmentsPerReview; round++)
                    repositories.Assessments.Save(new Assessment
                    {
                        ReviewId = reviewId, Summary = "s", Findings = [], RunId = runId, CreatedAt = DateTime.UtcNow,
                    });
            }

            test(repositories, runId);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { /* pooled connection may hold the file */ }
        }
    }

    private static GoldenJudgeRunSummary Run(
        RepositoryContext repositories, int runId, JudgePattern pattern, CountingLlmClient client, int repeat = 1,
        Action? onClientRequested = null, bool onePerCase = false, List<string>? progress = null) =>
        GoldenJudgeRun.Run(
            runId, repeat, pattern, "v1", Model, repositories,
            () =>
            {
                onClientRequested?.Invoke();
                return new Judge(client, "v1", pattern: pattern);
            },
            progress is null ? null : new ListProgress(progress),
            onePerCase);

    private sealed class ListProgress(List<string> lines) : IProgress<string>
    {
        public void Report(string value) => lines.Add(value);
    }

    [Fact]
    public void GoldenJudgeRunTests_A_run_with_no_assessments_makes_no_call_and_never_builds_a_judge()
    {
        WithRun(0, (repositories, runId) =>
        {
            var client = new CountingLlmClient();
            var judgeBuilt = false;

            var summary = Run(repositories, runId, JudgePattern.Sectioned, client, onClientRequested: () => judgeBuilt = true);

            Assert.Equal(0, summary.Assessments);
            Assert.Equal(0, client.Requests);
            Assert.False(judgeBuilt);
            Assert.Empty(repositories.Evaluations.List());
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_Every_assessment_of_the_run_is_judged_and_saved_with_the_pattern()
    {
        WithRun(2, (repositories, runId) =>
        {
            var client = new CountingLlmClient();

            var summary = Run(repositories, runId, JudgePattern.Sectioned, client);

            Assert.Equal(2, summary.JudgmentsMade);
            Assert.Equal(8, client.Requests);
            var evaluations = repositories.Evaluations.List();
            Assert.Equal(2, evaluations.Count);
            Assert.All(evaluations, e =>
            {
                Assert.Equal(JudgePattern.Sectioned, e.Pattern);
                Assert.Equal("v1", e.RubricVersion);
                Assert.Equal(Model, e.JudgeModel);
            });
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_Repeat_judges_each_assessment_that_many_times()
    {
        WithRun(2, (repositories, runId) =>
        {
            var client = new CountingLlmClient();

            Run(repositories, runId, JudgePattern.Single, client, repeat: 3);

            Assert.Equal(6, client.Requests);
            Assert.Equal(6, repositories.Evaluations.List().Count);
        });
    }

    [Theory]
    [InlineData(JudgePattern.Single, 1)]
    [InlineData(JudgePattern.Sectioned, 4)]
    public void GoldenJudgeRunTests_A_second_run_under_the_same_pattern_makes_no_further_calls(
        JudgePattern pattern, int callsPerJudgment)
    {
        WithRun(2, (repositories, runId) =>
        {
            var first = new CountingLlmClient();
            Run(repositories, runId, pattern, first, repeat: 2);
            Assert.Equal(2 * 2 * callsPerJudgment, first.Requests);

            var second = new CountingLlmClient();
            var judgeBuilt = false;
            var summary = Run(repositories, runId, pattern, second, repeat: 2, onClientRequested: () => judgeBuilt = true);

            Assert.Equal(0, second.Requests);
            Assert.False(judgeBuilt);
            Assert.Equal(0, summary.JudgmentsMade);
            Assert.Equal(4, summary.JudgmentsAlreadyStored);
            Assert.Equal(4, repositories.Evaluations.List().Count);
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_A_stored_evaluation_under_another_pattern_does_not_count_as_judged()
    {
        WithRun(1, (repositories, runId) =>
        {
            Run(repositories, runId, JudgePattern.Single, new CountingLlmClient());

            var sectioned = new CountingLlmClient();
            Run(repositories, runId, JudgePattern.Sectioned, sectioned);

            Assert.Equal(4, sectioned.Requests);
            Assert.Equal(
                [JudgePattern.Single, JudgePattern.Sectioned],
                repositories.Evaluations.List().Select(e => e.Pattern).ToArray());
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_An_interrupted_repeat_resumes_with_only_the_missing_judgments()
    {
        WithRun(1, (repositories, runId) =>
        {
            Run(repositories, runId, JudgePattern.Single, new CountingLlmClient(), repeat: 2);

            var resumed = new CountingLlmClient();
            Run(repositories, runId, JudgePattern.Single, resumed, repeat: 3);

            Assert.Equal(1, resumed.Requests);
            Assert.Equal(3, repositories.Evaluations.List().Count);
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_One_per_case_judges_the_lowest_id_assessment_of_each_review()
    {
        WithRun(2, 3, (repositories, runId) =>
        {
            var lowestIdPerReview = repositories.Assessments.List()
                .GroupBy(a => a.ReviewId).Select(g => g.Min(a => a.Id)).Order().ToArray();
            var progress = new List<string>();

            var summary = Run(repositories, runId, JudgePattern.Single, new CountingLlmClient(),
                onePerCase: true, progress: progress);

            Assert.Equal(2, summary.Assessments);
            Assert.Equal(lowestIdPerReview, repositories.Evaluations.List().Select(e => e.AssessmentId).Order().ToArray());
            Assert.Contains("Selected 2 of 6 assessments of run", progress[0]);
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_Without_one_per_case_every_assessment_of_the_run_is_judged()
    {
        WithRun(2, 3, (repositories, runId) =>
        {
            var summary = Run(repositories, runId, JudgePattern.Single, new CountingLlmClient());

            Assert.Equal(6, summary.Assessments);
            Assert.Equal(6, repositories.Evaluations.List().Count);
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_One_per_case_picks_the_same_assessments_on_every_invocation()
    {
        WithRun(2, 3, (repositories, runId) =>
        {
            Run(repositories, runId, JudgePattern.Single, new CountingLlmClient(), onePerCase: true);
            var firstPick = repositories.Evaluations.List().Select(e => e.AssessmentId).Order().ToArray();

            var second = new CountingLlmClient();
            Run(repositories, runId, JudgePattern.Single, second, onePerCase: true);

            Assert.Equal(0, second.Requests);
            Assert.Equal(firstPick, repositories.Evaluations.List().Select(e => e.AssessmentId).Order().ToArray());
        });
    }

    [Fact]
    public void GoldenJudgeRunTests_One_per_case_with_repeat_judges_each_selected_assessment_that_many_times_and_resumes_free()
    {
        WithRun(2, 3, (repositories, runId) =>
        {
            var first = new CountingLlmClient();
            Run(repositories, runId, JudgePattern.Single, first, repeat: 3, onePerCase: true);
            Assert.Equal(6, first.Requests);
            Assert.Equal(6, repositories.Evaluations.List().Count);

            var second = new CountingLlmClient();
            var summary = Run(repositories, runId, JudgePattern.Single, second, repeat: 3, onePerCase: true);

            Assert.Equal(0, second.Requests);
            Assert.Equal(0, summary.JudgmentsMade);
            Assert.Equal(6, repositories.Evaluations.List().Count);
        });
    }
}
