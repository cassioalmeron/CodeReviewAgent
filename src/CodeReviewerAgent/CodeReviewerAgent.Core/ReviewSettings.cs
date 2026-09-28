namespace CodeReviewerAgent.Core;

/// <summary>
/// The knobs of a review, resolved by the entry point and handed in.
/// <para>
/// Every one of these used to be an <c>Environment.GetEnvironmentVariable</c> call somewhere
/// inside Core (US-018). The values and their defaults are unchanged; what changed is who reads
/// them. Core reading the process environment cost three things: the settings were invisible at
/// the call site, so a reader of <c>Review()</c> could not tell what governed the run; the test
/// suite had to serialize every class that touched a variable, because variables are
/// process-wide; and a second caller in the same process (the Api, an MCP host) could not run
/// two reviews under different configurations.
/// </para>
/// <para>
/// The defaults are the ones the variables used to fall back to, so an instance built with a
/// prompt version alone behaves exactly like an unconfigured process did.
/// </para>
/// </summary>
/// <param name="PromptVersion">Which versioned review prompt to send (<c>PROMPT_VERSION</c>).</param>
/// <param name="Engine">
/// The engine name, used to estimate cost when the client does not report one
/// (<c>LLM_ENGINE</c>). Null estimates nothing, which is what an unset variable did.
/// </param>
/// <param name="SkillPromptVersion">Which versioned skill prompts to use (<c>SKILL_PROMPT_VERSION</c>).</param>
/// <param name="Skills">
/// The skill selection strategy (<c>SKILLS</c>): null or <c>all</c> lets the model pick,
/// <c>globs</c> selects mechanically, <c>off</c> selects nothing, and a comma-separated list
/// pins exactly those names.
/// </param>
public sealed record ReviewSettings(
    string PromptVersion,
    string? Engine = null,
    string SkillPromptVersion = "v1",
    string? Skills = null);
