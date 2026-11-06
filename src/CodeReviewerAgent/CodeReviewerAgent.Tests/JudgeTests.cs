using System.Collections.Concurrent;
using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Judge;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The pairwise call (plan 015, T1): the schema puts reasoning before the verdicts, verdicts
/// parse regardless of case, and the pairing is built from the two reviews' own prompt versions
/// rather than anything the model wrote.
/// </summary>
public class JudgeTests
{
    private static ReviewResult Review(string promptVersion, string summary) =>
        new(summary, [], "fake", "fake-model", promptVersion);

    [Fact]
    public void Evaluate_Pairwise_SchemaListsReasoningBeforeTheVerdictFields()
    {
        const string response = """
            {"reasoning":"x","correctness":"A","actionability":"A","calibration":"A",
             "signal_to_noise":"A","conciseness":"A","overall":"A"}
            """;
        var client = new FakeLlmClient(response);
        var judge = new Judge(client, "v2");

        judge.Evaluate("diff", Review("v3", "summary a"), Review("v5", "summary b"));

        var json = JsonSerializer.Serialize(client.LastRequestBody);
        Assert.True(
            json.IndexOf("\"reasoning\"", StringComparison.Ordinal)
                < json.IndexOf("\"correctness\"", StringComparison.Ordinal),
            "schema must list reasoning before the verdict fields, or the chain-of-thought claim is fiction");
    }

    [Fact]
    public void Evaluate_Pairwise_ParsesVerdicts_ToleratingCase()
    {
        const string response = """
            {
                "reasoning": "B is more concrete.",
                "correctness": "a",
                "actionability": "B",
                "calibration": "TIE",
                "signal_to_noise": "A",
                "conciseness": "tie",
                "overall": "b"
            }
            """;
        var client = new FakeLlmClient(response);
        var judge = new Judge(client, "v2");

        var outcome = judge.Evaluate("diff", Review("v3", "a"), Review("v5", "b"));

        Assert.Equal(Verdict.A, outcome.Judgment.Correctness);
        Assert.Equal(Verdict.B, outcome.Judgment.Actionability);
        Assert.Equal(Verdict.Tie, outcome.Judgment.Calibration);
        Assert.Equal(Verdict.A, outcome.Judgment.SignalToNoise);
        Assert.Equal(Verdict.Tie, outcome.Judgment.Conciseness);
        Assert.Equal(Verdict.B, outcome.Judgment.Overall);
        Assert.Equal("B is more concrete.", outcome.Judgment.Reasoning);
    }

    [Fact]
    public void Evaluate_Pairwise_BuildsThePairingFromTheReviewsGiven_NotFromTheModel()
    {
        const string response = """
            {"reasoning":"x","correctness":"A","actionability":"A","calibration":"A",
             "signal_to_noise":"A","conciseness":"A","overall":"A"}
            """;
        var client = new FakeLlmClient(response);
        var judge = new Judge(client, "v2");

        // "a" is v3 here; the pairing must reflect that regardless of what "A"/"B" the model wrote.
        var outcome = judge.Evaluate("diff", Review("v3", "summary a"), Review("v5", "summary b"));

        Assert.Equal("v3", outcome.Pairing.SlotA);
        Assert.Equal("v5", outcome.Pairing.SlotB);
    }

    // Answers each request from the rubric slice it carries and counts how many came in, so a
    // test can tell which criterion each call was about.
    private sealed class SectionAwareLlmClient : ILlmClient
    {
        private readonly Func<string, string> _answerFor;
        private int _requestCount;

        public SectionAwareLlmClient(Func<string, string> answerFor) => _answerFor = answerFor;

        public int RequestCount => _requestCount;

        public ConcurrentBag<Dictionary<string, object>> Requests { get; } = [];

        public MessageResponse Request(object requestBody)
        {
            Interlocked.Increment(ref _requestCount);
            var body = (Dictionary<string, object>)requestBody;
            Requests.Add(body);
            return new MessageResponse
            {
                Model = "fake-model",
                Content = [new ContentBlock { Type = "text", Text = _answerFor((string)body["system"]) }],
                Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
            };
        }
    }

    private static string CriterionOf(string system) =>
        new[] { "correctness", "actionability", "calibration", "signal_to_noise" }
            .Single(criterion => system.Contains($"- {criterion}:"));

    private static string Answer(int score, string rationale) =>
        $$"""{"score":{{score}},"rationale":"{{rationale}}"}""";

    private static SectionAwareLlmClient ClientScoring(
        int correctness, int actionability, int calibration, int signalToNoise) =>
        new(system => CriterionOf(system) switch
        {
            "correctness" => Answer(correctness, "r-correctness"),
            "actionability" => Answer(actionability, "r-actionability"),
            "calibration" => Answer(calibration, "r-calibration"),
            _ => Answer(signalToNoise, "r-signal"),
        });

    [Fact]
    public void JudgeTests_Sectioned_sends_four_requests_each_carrying_its_own_rubric_slice()
    {
        var client = ClientScoring(5, 4, 3, 2);
        var judge = new Judge(client, "v1", pattern: JudgePattern.Sectioned);

        judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(4, client.RequestCount);
        var criteria = client.Requests.Select(r => CriterionOf((string)r["system"])).OrderBy(c => c).ToArray();
        Assert.Equal(["actionability", "calibration", "correctness", "signal_to_noise"], criteria);
        Assert.All(client.Requests, r => Assert.Contains("Other judges", (string)r["system"]));
    }

    [Fact]
    public void JudgeTests_Single_sends_exactly_one_request()
    {
        var client = new SectionAwareLlmClient(_ =>
            """{"correctness":4,"actionability":4,"calibration":4,"signal_to_noise":4,"overall":4,"rationale":"x"}""");
        // The single rubric lists all four criteria, so the slice lookup does not apply here.
        var judge = new Judge(client, "v1");

        judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(1, client.RequestCount);
    }

    [Fact]
    public void JudgeTests_Sectioned_each_call_score_lands_on_the_matching_criterion()
    {
        var client = ClientScoring(5, 4, 3, 2);
        var judge = new Judge(client, "v1", pattern: JudgePattern.Sectioned);

        var outcome = judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(5, outcome.Judgment.Correctness);
        Assert.Equal(4, outcome.Judgment.Actionability);
        Assert.Equal(3, outcome.Judgment.Calibration);
        Assert.Equal(2, outcome.Judgment.SignalToNoise);
    }

    [Theory]
    [InlineData(5, 4, 3, 2, 4)]   // mean 3.5 rounds away from zero
    [InlineData(5, 5, 5, 4, 5)]   // mean 4.75
    [InlineData(2, 2, 2, 3, 2)]   // mean 2.25
    [InlineData(1, 1, 1, 1, 1)]
    public void JudgeTests_Sectioned_overall_is_the_rounded_mean_of_the_four_criteria(
        int correctness, int actionability, int calibration, int signalToNoise, int expectedOverall)
    {
        var client = ClientScoring(correctness, actionability, calibration, signalToNoise);
        var judge = new Judge(client, "v1", pattern: JudgePattern.Sectioned);

        var outcome = judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(expectedOverall, outcome.Judgment.Overall);
    }

    [Fact]
    public void JudgeTests_Sectioned_labels_the_rationales_and_sums_cost_and_tokens()
    {
        var client = ClientScoring(5, 4, 3, 2);
        var judge = new Judge(client, "v1", pattern: JudgePattern.Sectioned);

        var outcome = judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(
            "[correctness] r-correctness\n[actionability] r-actionability\n[calibration] r-calibration\n[signal-to-noise] r-signal",
            outcome.Judgment.Rationale);
        Assert.Equal(40, outcome.InputTokens);
        Assert.Equal(80, outcome.OutputTokens);
    }

    [Fact]
    public void JudgeTests_Sectioned_all_four_calls_receive_the_same_user_content_as_the_single_call()
    {
        var sectioned = ClientScoring(3, 3, 3, 3);
        var single = new SectionAwareLlmClient(_ =>
            """{"correctness":3,"actionability":3,"calibration":3,"signal_to_noise":3,"overall":3,"rationale":"x"}""");

        new Judge(sectioned, "v1", pattern: JudgePattern.Sectioned).Evaluate("the diff", Review("v3", "the summary"));
        new Judge(single, "v1").Evaluate("the diff", Review("v3", "the summary"));

        var expected = JsonSerializer.Serialize(single.Requests.Single()["messages"]);
        Assert.All(sectioned.Requests, r => Assert.Equal(expected, JsonSerializer.Serialize(r["messages"])));
    }

    [Fact]
    public void JudgeTests_Sectioned_a_call_with_unreadable_json_zeroes_only_its_criterion()
    {
        var client = new SectionAwareLlmClient(system => CriterionOf(system) switch
        {
            "calibration" => "this is not json",
            "correctness" => Answer(5, "ok"),
            "actionability" => Answer(4, "ok"),
            _ => Answer(3, "ok"),
        });
        var judge = new Judge(client, "v1", pattern: JudgePattern.Sectioned);

        var outcome = judge.Evaluate("diff", Review("v3", "summary"));

        Assert.Equal(4, client.RequestCount);
        Assert.Equal(5, outcome.Judgment.Correctness);
        Assert.Equal(4, outcome.Judgment.Actionability);
        Assert.Equal(0, outcome.Judgment.Calibration);
        Assert.Equal(3, outcome.Judgment.SignalToNoise);
        Assert.DoesNotContain("[calibration]", outcome.Judgment.Rationale);
    }
}
