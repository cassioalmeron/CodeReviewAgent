using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class AdvisorReviewTests
{
    private const string Diff = """
        diff --git a/App.cs b/App.cs
        --- a/App.cs
        +++ b/App.cs
        @@ -1,2 +1,4 @@
         existing line
        +var result = service.Process();
        +var total = items.Sum();
         trailing line
        """;

    private const string ConventionFirstReview = """
        {"summary":"first","findings":[{"file":"App.cs","code_snippet":"var result = service.Process();","severity":"info","category":"convention","problem":"Name is vague.","suggestion":"Rename."}]}
        """;

    private const string BugFirstReview = """
        {"summary":"first","findings":[{"file":"App.cs","code_snippet":"var result = service.Process();","severity":"critical","category":"bug","problem":"Result is never checked.","suggestion":"Check it."}]}
        """;

    private const string TwoHints = """
        {"hints":[{"file":"App.cs","snippet":"var total = items.Sum();","check":"Can items be null here?"},{"file":"App.cs","snippet":"","check":"Look at the whole new block."}]}
        """;

    // One grounded finding and one that cites a line the diff does not add.
    private const string SecondReview = """
        {"summary":"second","findings":[{"file":"App.cs","code_snippet":"var total = items.Sum();","severity":"warning","category":"bug","problem":"items may be null.","suggestion":"Guard it."},{"file":"App.cs","code_snippet":"a line that is not in the diff","severity":"warning","category":"bug","problem":"Invented.","suggestion":"None."}]}
        """;

    private static ReviewSettings AdvisorSettings => new(
        "v3", Engine: "openai", Pattern: ReviewSettings.AdvisorPattern, AdvisorEngine: "claude");

    [Fact]
    public void AdvisorReviewTests_A_first_review_holding_a_Bug_makes_one_call_and_does_not_escalate()
    {
        var cheap = new ScriptedLlmClient(Reply(BugFirstReview));
        var advisor = new ScriptedLlmClient();

        var result = Reviewer(cheap, advisor).Review();

        Assert.Single(cheap.Requests);
        Assert.Empty(advisor.Requests);
        Assert.False(result.Advisor!.Escalated);
        Assert.Equal(0, result.Advisor.Hints);
        Assert.Equal("first", result.Summary);
    }

    [Fact]
    public void AdvisorReviewTests_A_first_review_with_Convention_only_makes_three_calls()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        var result = Reviewer(cheap, advisor).Review();

        Assert.Equal(2, cheap.Requests.Count);
        Assert.Single(advisor.Requests);
        Assert.True(result.Advisor!.Escalated);
        Assert.Equal(2, result.Advisor.Hints);
    }

    [Fact]
    public void AdvisorReviewTests_The_advisor_request_carries_the_diff_and_the_first_findings()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        var userMessage = UserMessage(advisor.Requests[0]);
        Assert.Contains("+var result = service.Process();", userMessage);
        Assert.Contains("snippet: var result = service.Process();", userMessage);
        Assert.Contains("category: convention", userMessage);
        Assert.Contains("problem: Name is vague.", userMessage);
        Assert.Contains("You write no finding", SystemPrompt(advisor.Requests[0]));
    }

    [Fact]
    public void AdvisorReviewTests_The_advisor_schema_has_hints_only()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        var schema = JsonSerializer.SerializeToElement(advisor.Requests[0]).GetProperty("json_schema");
        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name);
        Assert.Equal(["hints"], properties);
    }

    [Fact]
    public void AdvisorReviewTests_The_second_review_keeps_the_first_system_prompt_and_adds_the_hints_after_the_diff()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        var firstSystem = SystemPrompt(cheap.Requests[0]);
        Assert.Contains("PascalCase", firstSystem);
        Assert.Equal(firstSystem, SystemPrompt(cheap.Requests[1]));

        var firstUser = UserMessage(cheap.Requests[0]);
        var secondUser = UserMessage(cheap.Requests[1]);
        Assert.StartsWith(firstUser, secondUser);
        var hints = secondUser[firstUser.Length..];
        Assert.Contains("Hints:", hints);
        Assert.Contains("- file: App.cs\n  snippet: var total = items.Sum();\n  check: Can items be null here?", hints);
        Assert.Contains("- file: App.cs\n  check: Look at the whole new block.", hints);
    }

    [Fact]
    public void AdvisorReviewTests_The_final_findings_are_the_second_reviews_grounded_against_the_diff()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        var result = Reviewer(cheap, advisor).Review();

        var finding = Assert.Single(result.Findings!);
        Assert.Equal("items may be null.", finding.Problem);
        Assert.Equal(3, finding.Line);
        Assert.Equal(1, result.DiscardedFindings);
        Assert.Equal("second", result.Summary);
    }

    [Fact]
    public void AdvisorReviewTests_The_escalated_review_sums_cost_and_tokens_of_the_three_calls_and_the_advisor_trace_is_its_own()
    {
        var cheap = new ScriptedLlmClient(
            Reply(ConventionFirstReview, cost: 0.01m, input: 100, output: 10),
            Reply(SecondReview, cost: 0.02m, input: 200, output: 20));
        var advisor = new ScriptedLlmClient(Reply(TwoHints, model: "strong-model", cost: 0.5m, input: 300, output: 30));

        var result = Reviewer(cheap, advisor).Review();

        // 0.01 + 0.5 + 0.02 plus the skill selection the fake selector reports (0.5, 7 and 3 tokens).
        Assert.Equal(1.03m, result.Cost);
        Assert.Equal(607, result.InputTokens);
        Assert.Equal(63, result.OutputTokens);
        Assert.Equal("cheap-model", result.Model);
        Assert.Equal(new Core.Advisor.AdvisorTrace(true, "strong-model", 0.5m, 300, 30, 2), result.Advisor);
    }

    [Fact]
    public void AdvisorReviewTests_An_advisor_that_reports_no_cost_is_estimated_with_the_advisor_engine()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints, model: "claude-sonnet-5", input: 1_000_000, output: 0));

        var result = Reviewer(cheap, advisor).Review();

        Assert.Equal(2m, result.Advisor!.Cost);
    }

    [Fact]
    public void AdvisorReviewTests_With_no_hints_the_second_review_is_skipped_and_the_first_review_stands()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview, cost: 0.01m, input: 100, output: 10), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply("""{"hints":[]}""", model: "strong-model", cost: 0.5m, input: 300, output: 30));

        var result = Reviewer(cheap, advisor).Review();

        Assert.Single(cheap.Requests);
        Assert.Single(advisor.Requests);
        Assert.Equal("first", result.Summary);
        Assert.Equal("Name is vague.", Assert.Single(result.Findings!).Problem);
        Assert.Equal(new Core.Advisor.AdvisorTrace(true, "strong-model", 0.5m, 300, 30, 0), result.Advisor);
        // 0.01 + 0.5 plus the skill selection the fake selector reports (0.5, 7 and 3 tokens).
        Assert.Equal(1.01m, result.Cost);
        Assert.Equal(407, result.InputTokens);
        Assert.Equal(43, result.OutputTokens);
        Assert.Equal("cheap-model", result.Model);
    }

    [Fact]
    public void AdvisorReviewTests_The_advisor_request_sends_max_tokens_16000()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        var maxTokens = JsonSerializer.SerializeToElement(advisor.Requests[0]).GetProperty("max_tokens").GetInt32();
        Assert.Equal(16000, maxTokens);
    }

    [Fact]
    public void AdvisorReviewTests_The_advisor_request_caps_the_reasoning_at_12000_inside_the_16000_budget()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        var request = JsonSerializer.SerializeToElement(advisor.Requests[0]);
        Assert.Equal(16000, request.GetProperty("max_tokens").GetInt32());
        Assert.Equal(12000, request.GetProperty("reasoning").GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public void AdvisorReviewTests_The_review_request_sends_no_reasoning()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(TwoHints));

        Reviewer(cheap, advisor).Review();

        Assert.False(JsonSerializer.SerializeToElement(cheap.Requests[0]).TryGetProperty("reasoning", out _));
    }

    [Fact]
    public void AdvisorReviewTests_A_cut_off_advisor_reply_is_a_failed_call_and_the_first_review_stands()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview, cost: 0.01m, input: 100, output: 10), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply("""{"hints":[{"file":"App.cs","snip""", model: "strong-model", cost: 0.5m, input: 300, output: 16000));

        var result = Reviewer(cheap, advisor).Review();

        Assert.Single(cheap.Requests);
        Assert.Equal(new Core.Advisor.AdvisorTrace(true, "strong-model", 0.5m, 300, 16000, 0, Failed: true), result.Advisor);
        Assert.Equal("first", result.Summary);
        Assert.Equal("Name is vague.", Assert.Single(result.Findings!).Problem);
        Assert.Equal(10 + 16000 + 3, result.OutputTokens);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    public void AdvisorReviewTests_An_empty_advisor_reply_is_a_failed_call_and_the_first_review_stands(string reply)
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply(reply, model: "strong-model", cost: 0.5m, input: 300, output: 16000));

        var result = Reviewer(cheap, advisor).Review();

        Assert.Single(cheap.Requests);
        Assert.Equal(new Core.Advisor.AdvisorTrace(true, "strong-model", 0.5m, 300, 16000, 0, Failed: true), result.Advisor);
        Assert.Equal("first", result.Summary);
        Assert.Equal("Name is vague.", Assert.Single(result.Findings!).Problem);
    }

    [Fact]
    public void AdvisorReviewTests_A_valid_empty_hints_reply_is_not_a_failed_call()
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview), Reply(SecondReview));
        var advisor = new ScriptedLlmClient(Reply("""{"hints":[]}"""));

        var result = Reviewer(cheap, advisor).Review();

        Assert.False(result.Advisor!.Failed);
    }

    [Fact]
    public void AdvisorReviewTests_The_advisor_pattern_without_an_advisor_client_throws_naming_it()
    {
        var exception = Assert.Throws<ArgumentException>(() => Reviewer(new ScriptedLlmClient(), null));

        Assert.Equal("advisor", exception.ParamName);
        Assert.Contains("advisor client", exception.Message);
    }

    [Fact]
    public void AdvisorReviewTests_An_unknown_pattern_message_lists_all_three_patterns()
    {
        var settings = new ReviewSettings("v3", Pattern: "other");

        var exception = Assert.Throws<ArgumentException>(
            () => new CodeReviewer(new ScriptedLlmClient(), Diff, settings, new FakeSkillSelector(), FakeSkillSource.Default));

        Assert.Contains("'single', 'voting' or 'advisor'", exception.Message);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("voting")]
    public void AdvisorReviewTests_Under_other_patterns_the_advisor_trace_is_null(string pattern)
    {
        var cheap = new ScriptedLlmClient(Reply(ConventionFirstReview));
        var settings = new ReviewSettings("v3", Pattern: pattern);
        var reviewer = new CodeReviewer(
            cheap, Diff, settings, new FakeSkillSelector("csharp"), FakeSkillSource.Default);

        var result = reviewer.Review();

        Assert.Null(result.Advisor);
    }

    private static CodeReviewer Reviewer(ILlmClient cheap, ILlmClient? advisor) =>
        new(cheap, Diff, AdvisorSettings, new FakeSkillSelector("csharp"), FakeSkillSource.Default, advisor: advisor);

    private static MessageResponse Reply(
        string text, string model = "cheap-model", decimal? cost = null, int input = 10, int output = 20) =>
        new()
        {
            Model = model,
            Content = [new ContentBlock { Type = "text", Text = text }],
            Usage = new Usage { InputTokens = input, OutputTokens = output },
            Cost = cost,
        };

    private static string SystemPrompt(object request) =>
        JsonSerializer.SerializeToElement(request).GetProperty("system").GetString()!;

    private static string UserMessage(object request) =>
        JsonSerializer.SerializeToElement(request).GetProperty("messages")[0].GetProperty("content").GetString()!;

    /// <summary>Answers each call with the next scripted response, repeating the last one, and keeps every request.</summary>
    private sealed class ScriptedLlmClient(params MessageResponse[] responses) : ILlmClient
    {
        public List<object> Requests { get; } = [];

        public MessageResponse Request(object requestBody)
        {
            // Voting calls the client from several threads.
            lock (Requests)
            {
                Requests.Add(requestBody);
                return responses[Math.Min(Requests.Count, responses.Length) - 1];
            }
        }
    }
}
