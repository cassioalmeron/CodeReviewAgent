using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// Serializes every test class that mutates the process environment.
/// <para>
/// Environment variables are process-wide and xUnit runs test classes in parallel, so two classes
/// setting the same variable interleave: one restores it in its <c>finally</c> while the other is
/// still mid-run. That produced a suite that passed four runs out of five, which is worse than a
/// missing test, because the one red run reads as a fluke and gets re-run instead of fixed.
/// </para>
/// <para>
/// Two known collisions, both real: <c>GOLDEN_RUNS</c> and <c>SKILLS</c> are set by both golden
/// test classes, and <c>EVAL_OUTPUT_DIR</c> is worse than a collision — <c>OutputPaths</c>
/// resolves once per process, so a class first touching it while another has the variable pointed
/// at a temp folder freezes that value for every test that follows.
/// </para>
/// <para>
/// The rule, so the next class does not reintroduce this: <b>if it calls
/// <c>Environment.SetEnvironmentVariable</c>, it belongs to this collection.</b> The whole suite
/// runs in about two seconds, so the lost parallelism costs nothing worth having.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public class EnvironmentCollection
{
    public const string Name = "process-environment";
}
