using CodeReviewerAgent.Core.Llm;
using Xunit;

namespace CodeReviewerAgent.Tests;

public class BudgetedLlmClientTests
{
    // A minimal ILlmClient that counts calls and returns a response built from whatever the test
    // wants (a provider-reported cost, or none, driving the token-based estimate instead).
    private sealed class CountingLlmClient(MessageResponse response) : ILlmClient
    {
        public int CallCount { get; private set; }

        public MessageResponse Request(object requestBody)
        {
            Interlocked.Increment(ref _callCount);
            CallCount = _callCount;
            return response;
        }

        private int _callCount;
    }

    private static MessageResponse WithCost(decimal cost) => new() { Cost = cost };

    private static MessageResponse WithTokens(string model, int input, int output) => new()
    {
        Model = model,
        Usage = new Usage { InputTokens = input, OutputTokens = output },
    };

    [Fact]
    public void BudgetedLlmClientTests_Under_the_cap_every_call_goes_through_and_the_response_is_unchanged()
    {
        var response = WithCost(1m);
        var inner = new CountingLlmClient(response);
        var client = new BudgetedLlmClient(inner, engine: null, model: null, new CostBudget(10m));

        var result = client.Request(new { });

        Assert.Same(response, result);
        Assert.Equal(1, inner.CallCount);
        Assert.Equal(1m, client.Spend);
    }

    [Fact]
    public void BudgetedLlmClientTests_Once_spend_reaches_the_cap_the_next_call_throws_and_the_inner_client_is_not_called()
    {
        var inner = new CountingLlmClient(WithCost(5m));
        var client = new BudgetedLlmClient(inner, engine: null, model: null, new CostBudget(5m));

        client.Request(new { }); // spend now equals the cap exactly
        Assert.Equal(1, inner.CallCount);

        Assert.Throws<BudgetExceededException>(() => client.Request(new { }));
        Assert.Equal(1, inner.CallCount); // the second call never reached the inner client
    }

    [Fact]
    public void BudgetedLlmClientTests_A_provider_reported_cost_is_preferred_over_the_estimate()
    {
        // Tokens alone, under "claude"/"claude-opus-4-8" pricing, would estimate far more than
        // the $0.01 the provider actually reports, so a wrong total here would mean the real
        // cost was ignored in favour of the estimate.
        var response = new MessageResponse
        {
            Cost = 0.01m,
            Model = "claude-opus-4-8",
            Usage = new Usage { InputTokens = 1_000_000, OutputTokens = 1_000_000 },
        };
        var inner = new CountingLlmClient(response);
        var client = new BudgetedLlmClient(inner, engine: "claude", model: "claude-opus-4-8", new CostBudget(10m));

        client.Request(new { });

        Assert.Equal(0.01m, client.Spend);
    }

    [Fact]
    public void BudgetedLlmClientTests_When_the_provider_reports_no_cost_the_token_estimate_is_used()
    {
        // gpt-4o-mini: $0.15 / 1M input, $0.60 / 1M output (CostCalculator).
        var inner = new CountingLlmClient(WithTokens("gpt-4o-mini", 1_000_000, 1_000_000));
        var client = new BudgetedLlmClient(inner, engine: "openai", model: "gpt-4o-mini", new CostBudget(10m));

        client.Request(new { });

        Assert.Equal(0.75m, client.Spend);
    }

    [Fact]
    public void BudgetedLlmClientTests_A_free_engine_never_trips_the_cap_however_many_calls_are_made()
    {
        var inner = new CountingLlmClient(WithTokens("llama3", 1_000_000, 1_000_000));
        var client = new BudgetedLlmClient(inner, engine: "ollama", model: "llama3", new CostBudget(0.0001m));

        for (var i = 0; i < 20; i++)
            client.Request(new { });

        Assert.Equal(20, inner.CallCount);
        Assert.Equal(0m, client.Spend);
    }

    [Fact]
    public void BudgetedLlmClientTests_Concurrent_calls_do_not_lose_spend()
    {
        var inner = new CountingLlmClient(WithCost(0.01m));
        // High enough that no concurrent call trips the cap mid-flight; the point of this test is
        // that the accumulator itself is race-free, not the in-flight-overshoot behaviour.
        var client = new BudgetedLlmClient(inner, engine: null, model: null, new CostBudget(1000m));

        const int callCount = 200;
        Parallel.For(0, callCount, _ => client.Request(new { }));

        Assert.Equal(callCount, inner.CallCount);
        Assert.Equal(callCount * 0.01m, client.Spend);
    }

    [Fact]
    public void BudgetedLlmClientTests_The_exception_message_contains_both_the_spend_and_the_cap()
    {
        var inner = new CountingLlmClient(WithCost(3m));
        var client = new BudgetedLlmClient(inner, engine: null, model: null, new CostBudget(3m));
        client.Request(new { });

        var ex = Assert.Throws<BudgetExceededException>(() => client.Request(new { }));

        Assert.Contains("3", ex.Message);
        Assert.Equal(3m, ex.Spend);
        Assert.Equal(3m, ex.Cap);
    }

    [Fact]
    public void BudgetedLlmClientTests_The_exception_message_shows_a_sub_cent_spend_as_a_real_number_not_zero()
    {
        // A full golden set pass costs about $0.0012 in this project; "F2" rounds that to
        // "$0.00", which would make the one number that matters look like nothing was spent.
        var inner = new CountingLlmClient(WithCost(0.0012m));
        var client = new BudgetedLlmClient(inner, engine: null, model: null, new CostBudget(0.0012m));
        client.Request(new { });

        var ex = Assert.Throws<BudgetExceededException>(() => client.Request(new { }));

        Assert.Contains("0.0012", ex.Message);
        Assert.DoesNotContain("$0.00 ", ex.Message);
    }

    [Fact]
    public void BudgetedLlmClientTests_Two_decorators_sharing_one_budget_trip_on_the_combined_spend_not_individually()
    {
        // The `all` command builds an executor client and a judge client over the same run; with
        // a shared CostBudget, the second decorator's first call already sees what the first
        // decorator spent, so the pair trips together instead of each getting its own cap.
        var budget = new CostBudget(5m);
        var executor = new BudgetedLlmClient(new CountingLlmClient(WithCost(3m)), engine: null, model: null, budget);
        var judge = new BudgetedLlmClient(new CountingLlmClient(WithCost(3m)), engine: null, model: null, budget);

        executor.Request(new { }); // spend: 3
        judge.Request(new { });    // spend: 6 — already over the 5 cap, but this call was in flight

        Assert.Equal(6m, budget.Spend);
        Assert.Equal(6m, executor.Spend);
        Assert.Equal(6m, judge.Spend);

        // The next call on either decorator sees the shared total and stops.
        Assert.Throws<BudgetExceededException>(() => executor.Request(new { }));
        Assert.Throws<BudgetExceededException>(() => judge.Request(new { }));
    }
}
