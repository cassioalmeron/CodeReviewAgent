namespace CodeReviewerAgent.Core.Advisor;

/// <summary>
/// What the advisor pattern did with one review. When it did not escalate, the model is null and
/// every number is zero; when it did, the model, cost and tokens are the advisor call alone.
/// <paramref name="Failed"/> marks an advisor reply that was empty or not readable as JSON: the call
/// was billed, there are no hints, and the first review stands. A trace recorded without the field
/// reads as not failed.
/// </summary>
public sealed record AdvisorTrace(
    bool Escalated, string? Model, decimal Cost, int InputTokens, int OutputTokens, int Hints, bool Failed = false);
