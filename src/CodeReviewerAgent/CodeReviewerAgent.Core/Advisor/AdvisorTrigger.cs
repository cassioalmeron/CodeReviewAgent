namespace CodeReviewerAgent.Core.Advisor;

/// <summary>
/// Decides whether a review should be escalated to the advisor, from the findings alone: no model
/// call, no judgement. A review with no Bug and no Security finding is the shape of a miss.
/// </summary>
public static class AdvisorTrigger
{
    // A constant, not an environment variable: a version that lives in two places lets a
    // measurement report a configuration other than the one that ran.
    public const string Version = "v1";

    public static bool ShouldEscalate(IReadOnlyList<Finding> findings) =>
        !findings.Any(f => f.Category is Category.Bug or Category.Security);
}
