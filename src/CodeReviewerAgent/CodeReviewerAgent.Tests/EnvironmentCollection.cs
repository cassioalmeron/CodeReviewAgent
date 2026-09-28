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
/// The collisions that caused it are gone. They were all in classes covering Core, which read
/// <c>GOLDEN_RUNS</c>, <c>SKILLS</c>, <c>GOLDEN_PARALLELISM</c>, <c>SKILL_EVAL_RUNS</c>,
/// <c>PROMPT_VERSION</c> and <c>EVAL_OUTPUT_DIR</c> from inside the production code, so a test
/// could only configure a run by moving process-wide state. Core takes its settings as arguments
/// now (US-018), and those classes left this collection: they configure a run at the call and no
/// longer touch the environment at all. What remains here is the one class that tests reading the
/// environment, which is Infra's job and belongs there.
/// </para>
/// <para>
/// The rule, so the next class does not reintroduce the problem: <b>if it calls
/// <c>Environment.SetEnvironmentVariable</c>, it belongs to this collection.</b> The whole suite
/// runs in about two seconds, so the lost parallelism costs nothing worth having.
/// </para>
/// </summary>
[CollectionDefinition(Name)]
public class EnvironmentCollection
{
    public const string Name = "process-environment";
}
