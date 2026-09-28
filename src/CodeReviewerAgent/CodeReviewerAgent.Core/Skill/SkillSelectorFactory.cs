using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Core.Skill;

/// <summary>
/// Selects the <see cref="ISkillSelector"/> strategy from <c>SKILLS</c>:
/// <list type="bullet">
///   <item><c>all</c> (or unset) — the model picks from the catalog (no fallback)</item>
///   <item><c>globs</c> — the mechanical <c>applies-to</c> selection, no LLM call</item>
///   <item><c>off</c> — no skills at all</item>
///   <item><c>&lt;name&gt;,&lt;name&gt;</c> — exactly these, no LLM call</item>
/// </list>
/// </summary>
/// <remarks>
/// The setting arrives as a parameter: it used to be read from the environment here, which made
/// the strategy of a run invisible at the call site (US-018).
/// </remarks>
public static class SkillSelectorFactory
{
    public static ISkillSelector Create(
        ILlmClient client, string? setting,
        string skillPromptVersion = SkillPrompt.DefaultVersion, string? engine = null,
        IProgress<string>? progress = null) =>
        (setting ?? "").Trim().ToLowerInvariant() switch
        {
            "" or "all" => new LlmSkillSelector(client, skillPromptVersion, engine, progress),
            "globs" => new GlobSkillSelector(),
            "off" => new NoSkillSelector(),
            var names => new ExplicitSkillSelector(
                names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
        };
}
