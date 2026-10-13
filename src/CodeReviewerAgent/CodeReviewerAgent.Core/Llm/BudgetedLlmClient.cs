namespace CodeReviewerAgent.Core.Llm;

/// <summary>
/// The cap (USD) and accumulated spend for one process run, shared by every
/// <see cref="BudgetedLlmClient"/> built in that run. The provider's own cap protects the
/// account; this one protects a single <c>dotnet run</c> — the limit counts one process execution
/// only, starting from zero every time, with no database and no history carried across runs. That
/// is also why one <see cref="CostBudget"/> must be handed to every decorator the run builds: a
/// cap per client, rather than per run, is not the limit that was asked for — the `all` command
/// alone builds an executor and a judge client, and each would get its own cap to overshoot.
/// <para>
/// <see cref="EnsureNotExceeded"/> and <see cref="Add"/> are deliberately two calls rather than
/// one "charge" — a call's cost is unknowable until it returns, so the budget can only be asked
/// "am I already over?" before the call, and told what it cost after.
/// </para>
/// </summary>
public sealed class CostBudget(decimal capUsd)
{
    private readonly Lock _lock = new();
    private decimal _spend;

    public decimal Cap { get; } = capUsd;

    public decimal Spend
    {
        get
        {
            lock (_lock)
                return _spend;
        }
    }

    /// <summary>Throws <see cref="BudgetExceededException"/> when the cap has already been reached.</summary>
    public void EnsureNotExceeded()
    {
        lock (_lock)
        {
            if (_spend >= Cap)
                throw new BudgetExceededException(_spend, Cap);
        }
    }

    /// <summary>Adds a completed call's cost to the running total.</summary>
    public void Add(decimal cost)
    {
        lock (_lock)
            _spend += cost;
    }
}

/// <summary>
/// Decorates an <see cref="ILlmClient"/> so every call is checked against a shared
/// <see cref="CostBudget"/> before it goes out, and accounted for afterwards.
/// <para>
/// <b>Honest limitation:</b> a call's cost is unknowable until it returns, so the cap is checked
/// only against what has <em>already</em> been spent, never against what is in flight. Several
/// calls can be mid-request when the cap is reached — the golden set issues up to
/// <c>GOLDEN_PARALLELISM</c> of them at once, and the `all` command runs an executor and a judge
/// client over the same budget — and every one of those already past the check will still
/// complete and add its cost, so the final spend can overshoot the cap by up to one call's cost
/// per parallel slot. This decorator stops the <em>next</em> call, not the ones already running.
/// </para>
/// </summary>
public sealed class BudgetedLlmClient(ILlmClient inner, string? engine, string? model, CostBudget budget) : ILlmClient
{
    public decimal Spend => budget.Spend;

    public MessageResponse Request(object requestBody)
    {
        budget.EnsureNotExceeded();

        var response = inner.Request(requestBody);

        // Same expression the rest of the codebase uses (CodeReviewer, LlmSkillSelector): the
        // provider's own reported cost wins when there is one (OpenRouter), else the estimate
        // from tokens, priced under whichever model actually answered.
        var cost = response?.Cost
            ?? CostCalculator.Estimate(engine, response?.Model ?? model,
                response?.Usage?.InputTokens ?? 0, response?.Usage?.OutputTokens ?? 0);

        budget.Add(cost);

        return response!;
    }
}

/// <summary>
/// Thrown by <see cref="CostBudget"/> when the accumulated spend has already reached the
/// configured cap: the run stopped before the next call went out.
/// </summary>
public sealed class BudgetExceededException(decimal spend, decimal cap) : Exception(
    $"Cost limit exceeded: already spent {Utils.Money(spend)} of a {Utils.Money(cap)} cap. " +
    "The run stopped before the next call.")
{
    public decimal Spend { get; } = spend;

    public decimal Cap { get; } = cap;
}
