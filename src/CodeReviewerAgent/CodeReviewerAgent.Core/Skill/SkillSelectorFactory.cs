namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Selects the <see cref="ISkillSelector"/> strategy from <c>SKILLS</c>:
    /// <list type="bullet">
    ///   <item><c>all</c> (or unset) — the model picks from the catalog (no fallback)</item>
    ///   <item><c>globs</c> — the mechanical <c>applies-to</c> selection, no LLM call</item>
    ///   <item><c>off</c> — no skills at all</item>
    ///   <item><c>&lt;name&gt;,&lt;name&gt;</c> — exactly these, no LLM call</item>
    /// </list>
    /// </summary>
    public static class SkillSelectorFactory
    {
        public static ISkillSelector Create(ILlmClient client) =>
            Create(client, Environment.GetEnvironmentVariable("SKILLS"));

        internal static ISkillSelector Create(ILlmClient client, string? setting) =>
            (setting ?? "").Trim().ToLowerInvariant() switch
            {
                "" or "all" => new LlmSkillSelector(client),
                "globs" => new GlobSkillSelector(),
                "off" => new NoSkillSelector(),
                var names => new ExplicitSkillSelector(
                    names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            };
    }
}
