using System.Text;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Formats the two prompt fragments the skill flow needs: the catalog offered to the
    /// selection call (tier 1) and the instructions section appended to the review system
    /// prompt (tier 2). Both fragments' wording lives in versioned files under
    /// <c>prompts/</c>, like the review prompt and the judge rubric — only the assembly of the
    /// skill data around them happens here.
    /// </summary>
    public static class SkillPrompt
    {
        /// <summary>The version of the skill prompts to use (<c>SKILL_PROMPT_VERSION</c>).</summary>
        public static string Version =>
            Environment.GetEnvironmentVariable("SKILL_PROMPT_VERSION") ?? "v1";

        /// <summary>
        /// The system prompt of the selection call: <c>prompts/skill-selection-&lt;version&gt;.md</c>
        /// followed by the catalog. The catalog carries no <c>location</c>: the engines that run
        /// the review can't read files, and the activated block carries the skill directory when
        /// it matters.
        /// </summary>
        public static string Selection(IReadOnlyList<SkillRef> catalog, string version)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine(Load("skill-selection", version));
            prompt.AppendLine();
            prompt.AppendLine("<available_skills>");
            foreach (var skill in catalog)
            {
                prompt.AppendLine("  <skill>");
                prompt.AppendLine($"    <name>{skill.Name}</name>");
                prompt.AppendLine($"    <description>{skill.Description}</description>");
                prompt.AppendLine("  </skill>");
            }
            prompt.AppendLine("</available_skills>");
            return prompt.ToString();
        }

        /// <summary>
        /// The guidelines section appended to the review system prompt
        /// (<c>prompts/skill-guidelines-&lt;version&gt;.md</c>) wrapping the activated skills.
        /// Returns an empty string when nothing was activated.
        /// </summary>
        public static string Guidelines(IReadOnlyList<ActivatedSkill> skills, string version)
        {
            if (skills.Count == 0)
                return string.Empty;

            var section = new StringBuilder();
            section.AppendLine();
            section.AppendLine(Load("skill-guidelines", version));
            foreach (var skill in skills)
            {
                section.AppendLine();
                section.Append(skill.Render());
            }
            return section.ToString();
        }

        private static string Load(string prompt, string version)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "prompts", $"{prompt}-{version}.md");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Skill prompt not found: {path}");
            return File.ReadAllText(path).TrimEnd();
        }
    }
}
