namespace CodeReviewerAgent.Core.Advisor;

/// <summary>Where the advisor says the second review should look, and what to check there. It is never a finding.</summary>
public sealed record AdvisorHint(string File, string? Snippet, string Check);
