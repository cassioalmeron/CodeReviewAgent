namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// What a golden run needs beyond the client, the cases and the prompt versions.
/// <para>
/// All of it used to be read from the environment inside the evaluator (US-018), which is why
/// the golden tests had to set process-wide variables to control a run and serialize against
/// each other to do it. The defaults are the fallbacks those variables had, so an instance
/// built with no arguments runs exactly what an unconfigured process ran.
/// </para>
/// </summary>
/// <param name="Runs">How many times each case is reviewed (<c>GOLDEN_RUNS</c>).</param>
/// <param name="Parallelism">
/// How many paid rounds go out at once (<c>GOLDEN_PARALLELISM</c>). Capped on purpose:
/// uncapped concurrency trades latency for 429s, and the transport's backoff gives the time
/// back with interest.
/// </param>
/// <param name="Skills">
/// The skill strategy every round runs under (<c>SKILLS</c>). It is also what the run's
/// condition is recorded as, so the report says which strategy produced the rates.
/// </param>
/// <param name="Engine">The engine name, for estimating cost when the client reports none (<c>LLM_ENGINE</c>).</param>
/// <param name="SkillPromptVersion">Which versioned skill prompts the rounds use (<c>SKILL_PROMPT_VERSION</c>).</param>
public sealed record GoldenSettings(
    int Runs = 3,
    int Parallelism = 4,
    string? Skills = null,
    string? Engine = null,
    string SkillPromptVersion = Skill.SkillPrompt.DefaultVersion)
{
    /// <summary>The settings one side of the run reviews under.</summary>
    public ReviewSettings Review(string promptVersion) =>
        new(promptVersion, Engine, SkillPromptVersion, Skills);
}
