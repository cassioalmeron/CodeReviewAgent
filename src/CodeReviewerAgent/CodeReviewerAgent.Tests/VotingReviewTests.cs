using System.Collections.Concurrent;
using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class VotingReviewTests
{
    private static readonly string[] Subjects = ["bug", "security", "performance", "convention"];

    private const string Diff = """
        diff --git a/App.cs b/App.cs
        --- a/App.cs
        +++ b/App.cs
        @@ -1,2 +1,3 @@
         existing line
        +var result = service.Process();
         trailing line
        """;

    private static ReviewSettings Voting => new("v3", Pattern: "voting");

    private static string Finding(string category, string snippet = "var result = service.Process();") => $$"""
        {"file":"App.cs","code_snippet":"{{snippet}}","severity":"warning","category":"{{category}}","problem":"p","suggestion":"s"}
        """;

    private static string Response(string summary, params string[] findings) =>
        $$"""{"summary":"{{summary}}","findings":[{{string.Join(",", findings)}}]}""";

    private static CodeReviewer Reviewer(VotingFakeClient client, ReviewSettings? settings = null, FakeSkillSelector? selector = null) =>
        new(client, Diff, settings ?? Voting, selector ?? new FakeSkillSelector(), FakeSkillSource.Default);

    [Fact]
    public void VotingReviewTests_A_finding_with_a_category_outside_its_review_is_dropped_and_counted_apart()
    {
        var client = new VotingFakeClient(subject => subject switch
        {
            "bug" => Response("bug summary", Finding("bug"), Finding("security"), Finding("style")),
            "convention" => Response("convention summary", Finding("maintainability"), Finding("bug"), Finding("convention", "var missing = 1;")),
            _ => Response("none"),
        });

        var result = Reviewer(client).Review();

        Assert.Equal([Category.Bug, Category.Maintainability], result.Findings!.Select(f => f.Category).ToArray());
        Assert.Equal(3, result.OutOfScopeFindings);
        Assert.Equal(1, result.DiscardedFindings);
    }

    [Fact]
    public void VotingReviewTests_Two_findings_on_the_same_line_from_different_reviews_both_stay()
    {
        var client = new VotingFakeClient(subject => subject switch
        {
            "bug" => Response("b", Finding("bug")),
            "security" => Response("s", Finding("security")),
            _ => Response("none"),
        });

        var result = Reviewer(client).Review();

        Assert.Equal(2, result.Findings!.Count);
        Assert.All(result.Findings, f => Assert.Equal(2, f.Line));
        Assert.Equal([Category.Bug, Category.Security], result.Findings.Select(f => f.Category).ToArray());
    }

    [Fact]
    public void VotingReviewTests_Cost_and_tokens_are_the_sum_of_the_four_calls_plus_the_skill_selection()
    {
        var client = new VotingFakeClient(_ => Response("x"), cost: 0.25m);

        var result = Reviewer(client, selector: new FakeSkillSelector("csharp")).Review();

        Assert.Equal(4, client.CallCount);
        Assert.Equal((4 * 10) + 7, result.InputTokens);
        Assert.Equal((4 * 20) + 3, result.OutputTokens);
        Assert.Equal((4 * 0.25m) + 0.5m, result.Cost);
    }

    [Fact]
    public void VotingReviewTests_The_four_reviews_run_at_the_same_time_and_latency_is_the_wall_time_of_the_block()
    {
        var client = new VotingFakeClient(_ => Response("x"), delay: TimeSpan.FromMilliseconds(300), requireAllFourAtOnce: true);

        var result = Reviewer(client).Review();

        Assert.Equal(4, client.CallCount);
        Assert.True(result.LatencyMs >= 300, $"Latency {result.LatencyMs} ms is shorter than one call.");
        Assert.True(result.LatencyMs < 1000, $"Latency {result.LatencyMs} ms looks like the sum of the four calls.");
    }

    [Fact]
    public void VotingReviewTests_The_prompt_version_stays_the_base_one_and_each_review_loads_its_own_prompt()
    {
        var client = new VotingFakeClient(_ => Response("x"));

        var result = Reviewer(client).Review();

        Assert.Equal("v3", result.PromptVersion);
        Assert.Equal(Subjects.Order().ToArray(), client.Subjects.Order().ToArray());
    }

    [Fact]
    public void VotingReviewTests_Skills_are_selected_once_and_every_review_carries_them()
    {
        var selector = new FakeSkillSelector("csharp");
        var client = new VotingFakeClient(_ => Response("x"));

        var result = Reviewer(client, selector: selector).Review();

        Assert.Equal(1, selector.Calls);
        Assert.Equal("csharp", result.Skills);
        Assert.Equal(4, client.SystemPrompts.Count);
        Assert.All(client.SystemPrompts, p => Assert.Contains("PascalCase", p));
    }

    [Fact]
    public void VotingReviewTests_The_summary_keeps_every_review_text_labelled_by_subject()
    {
        var client = new VotingFakeClient(subject => subject == "performance" ? Response("") : Response($"{subject} said hi"));

        var result = Reviewer(client).Review();

        Assert.Equal(
            "[bug] bug said hi\n[security] security said hi\n[convention] convention said hi",
            result.Summary);
    }

    [Fact]
    public void VotingReviewTests_The_single_pattern_makes_one_call_and_counts_nothing_out_of_scope()
    {
        var client = new VotingFakeClient(_ => Response("only", Finding("bug"), Finding("security")));

        var result = Reviewer(client, new ReviewSettings("v3")).Review();

        Assert.Equal(1, client.CallCount);
        Assert.Equal(2, result.Findings!.Count);
        Assert.Equal(0, result.OutOfScopeFindings);
        Assert.Equal("only", result.Summary);
    }

    [Fact]
    public void VotingReviewTests_An_unknown_pattern_is_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Reviewer(new VotingFakeClient(_ => "{}"), new ReviewSettings("v3", Pattern: "quorum")));

        Assert.Contains("quorum", ex.Message);
    }

    /// <summary>
    /// Answers each review according to the subject whose prompt it was sent with. With
    /// <c>requireAllFourAtOnce</c> a call waits for the other three, so a sequential run fails.
    /// </summary>
    private sealed class VotingFakeClient : ILlmClient
    {
        private readonly Func<string, string> _respond;
        private readonly decimal? _cost;
        private readonly TimeSpan _delay;
        private readonly Barrier? _barrier;
        private readonly ConcurrentBag<string> _subjects = [];
        private readonly ConcurrentBag<string> _systemPrompts = [];

        public VotingFakeClient(
            Func<string, string> respond, decimal? cost = null, TimeSpan delay = default, bool requireAllFourAtOnce = false)
        {
            _respond = respond;
            _cost = cost;
            _delay = delay;
            _barrier = requireAllFourAtOnce ? new Barrier(4) : null;
        }

        public int CallCount => _subjects.Count;

        public IReadOnlyCollection<string> Subjects => _subjects;

        public IReadOnlyCollection<string> SystemPrompts => _systemPrompts;

        public MessageResponse Request(object requestBody)
        {
            var system = JsonSerializer.SerializeToElement(requestBody).GetProperty("system").GetString()!;
            _systemPrompts.Add(system);
            var subject = SubjectOf(system);
            _subjects.Add(subject);

            if (_barrier is not null && !_barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The four reviews did not run at the same time.");

            if (_delay > TimeSpan.Zero)
                Thread.Sleep(_delay);

            return new MessageResponse
            {
                Model = "fake-model",
                Content = [new ContentBlock { Type = "text", Text = _respond(subject) }],
                Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
                Cost = _cost,
            };
        }

        private static string SubjectOf(string system)
        {
            foreach (var subject in VotingReviewTests.Subjects)
                if (system.StartsWith(File.ReadAllText(PromptPath(subject)), StringComparison.Ordinal))
                    return subject;

            return "single";
        }

        private static string PromptPath(string subject) =>
            Path.Combine(AppContext.BaseDirectory, "assets", "prompts", $"review-v3-{subject}.md");
    }
}
