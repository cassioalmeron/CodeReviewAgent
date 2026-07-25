using System.Text;
using System.Text.RegularExpressions;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Loads project guidelines ("skills") from <c>skills/&lt;name&gt;/SKILL.md</c> and
    /// selects the ones that apply to a diff. Each skill declares an <c>applies-to</c>
    /// glob (comma-separated) in its frontmatter; a skill is included when any diff file
    /// matches. The selected skills are formatted into a section appended to the review
    /// system prompt so the LLM reports violations as <c>convention</c> findings.
    /// </summary>
    public static partial class SkillLoader
    {
        private sealed record Skill(string Name, string[] Globs, string Body);

        /// <summary>
        /// Builds the "Project guidelines" prompt section for the files touched by the diff,
        /// or an empty string when no skill applies.
        /// </summary>
        public static string BuildGuidelines(IEnumerable<string> filePaths)
        {
            var paths = filePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
            if (paths.Count == 0)
                return string.Empty;

            var applicable = Load()
                .Where(s => s.Globs.Any(g => paths.Any(p => Matches(g, p))))
                .ToList();
            if (applicable.Count == 0)
            {
                return string.Empty;
            }

            var section = new StringBuilder();
            section.AppendLine();
            section.AppendLine("# Project guidelines");
            section.AppendLine();
            section.AppendLine(
                "The project enforces the conventions below. In addition to the review above, "
                + "report every ADDED line that violates a guideline as a finding with "
                + "`category: convention`, following the same grounding rules.");
            foreach (var skill in applicable)
            {
                section.AppendLine();
                section.AppendLine(skill.Body.Trim());
            }
            return section.ToString();
        }

        private static IEnumerable<Skill> Load()
        {
            var root = Path.Combine(AppContext.BaseDirectory, "skills");
            if (!Directory.Exists(root))
                yield break;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var path = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(path))
                    continue;
                var skill = Parse(Path.GetFileName(dir), File.ReadAllText(path));
                if (skill is not null)
                    yield return skill;
            }
        }

        // Splits the `---` frontmatter from the body and reads the `applies-to` globs.
        private static Skill? Parse(string name, string text)
        {
            var normalized = text.Replace("\r\n", "\n");
            var match = FrontmatterRegex().Match(normalized);
            if (!match.Success)
                return null;

            var appliesTo = AppliesToRegex().Match(match.Groups["frontmatter"].Value);
            if (!appliesTo.Success)
                return null;

            var globs = appliesTo.Groups["value"].Value
                .Trim().Trim('"', '\'')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (globs.Length == 0)
                return null;

            return new Skill(name, globs, match.Groups["body"].Value);
        }

        // A glob matches on the full path; a glob without a slash matches on the file name.
        private static bool Matches(string glob, string path)
        {
            var target = glob.Contains('/') ? path.Replace('\\', '/') : Path.GetFileName(path);
            return Regex.IsMatch(target, GlobToRegex(glob), RegexOptions.IgnoreCase);
        }

        private static string GlobToRegex(string glob)
        {
            var pattern = new StringBuilder("^");
            for (var i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                switch (c)
                {
                    case '*':
                        if (i + 1 < glob.Length && glob[i + 1] == '*')
                        {
                            pattern.Append(".*");
                            i++;
                        }
                        else
                            pattern.Append("[^/]*");
                        break;
                    case '?':
                        pattern.Append("[^/]");
                        break;
                    default:
                        pattern.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }
            pattern.Append('$');
            return pattern.ToString();
        }

        [GeneratedRegex(@"^---\n(?<frontmatter>.*?)\n---\n(?<body>.*)$", RegexOptions.Singleline)]
        private static partial Regex FrontmatterRegex();

        [GeneratedRegex(@"^applies-to:\s*(?<value>.+)$", RegexOptions.Multiline)]
        private static partial Regex AppliesToRegex();
    }
}
