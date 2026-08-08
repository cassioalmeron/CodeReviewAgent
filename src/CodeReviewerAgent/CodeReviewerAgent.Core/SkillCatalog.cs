using System.Text;
using System.Text.RegularExpressions;

namespace CodeReviewerAgent.Core
{
    public enum SkillDiagnosticLevel
    {
        Warning,
        Error,
    }

    /// <summary>A validation note recorded during discovery, surfaced by the <c>skills</c> command.</summary>
    public sealed record SkillDiagnostic(string Path, SkillDiagnosticLevel Level, string Message);

    /// <summary>
    /// A catalog entry (tier 1): what the model needs to decide whether the skill is relevant,
    /// without its instructions. <see cref="Location"/> is the absolute path to the
    /// <c>SKILL.md</c>; <see cref="Directory"/> is its parent, the base for relative paths.
    /// </summary>
    public sealed record SkillRef(string Name, string Description, string Location, string Directory)
    {
        public IReadOnlyDictionary<string, string> Metadata { get; init; } =
            new Dictionary<string, string>();
    }

    /// <summary>
    /// A skill loaded for injection (tier 2): the markdown body with the frontmatter stripped,
    /// plus the bundled resources (tier 3) enumerated but deliberately not read.
    /// </summary>
    public sealed record ActivatedSkill(
        string Name, string Directory, string Body, IReadOnlyList<string> Resources)
    {
        /// <summary>True when the resource listing was capped at <see cref="SkillCatalog.MaxResources"/>.</summary>
        public bool Truncated { get; init; }

        /// <summary>
        /// Renders the block injected into the review prompt. The structured tags let the model
        /// tell skill instructions apart from the rest of the prompt; the directory and resource
        /// listing are only emitted when the skill actually ships files.
        /// </summary>
        public string Render()
        {
            var block = new StringBuilder();
            block.AppendLine($"<skill_content name=\"{Name}\">");
            block.AppendLine(Body.Trim());
            if (Resources.Count > 0)
            {
                block.AppendLine();
                block.AppendLine($"Skill directory: {Directory}");
                block.AppendLine("Relative paths in this skill are relative to the skill directory.");
                block.AppendLine();
                block.AppendLine("<skill_resources>");
                foreach (var resource in Resources)
                    block.AppendLine($"  <file>{resource}</file>");
                if (Truncated)
                    block.AppendLine($"  <!-- listing capped at {SkillCatalog.MaxResources} files -->");
                block.AppendLine("</skill_resources>");
            }
            block.AppendLine("</skill_content>");
            return block.ToString();
        }
    }

    /// <summary>
    /// Discovers the bundled skills (<c>skills/&lt;name&gt;/SKILL.md</c>) and loads them on demand,
    /// following the Agent Skills specification: the catalog carries only <c>name</c> and
    /// <c>description</c>, the body is read at activation time, and validation is lenient — cosmetic
    /// problems are reported as diagnostics instead of dropping the skill.
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>Cap on the resources listed for a single skill.</summary>
        public const int MaxResources = 20;

        private const int MaxNameLength = 64;

        public static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "skills");

        /// <summary>
        /// Scans <paramref name="root"/> (the bundled <c>skills/</c> directory by default) and
        /// returns the catalog plus the diagnostics collected while validating it.
        /// </summary>
        public static (IReadOnlyList<SkillRef> Skills, IReadOnlyList<SkillDiagnostic> Diagnostics) Discover(
            string? root = null)
        {
            var skills = new List<SkillRef>();
            var diagnostics = new List<SkillDiagnostic>();

            root ??= DefaultRoot;
            if (!Directory.Exists(root))
                return (skills, diagnostics);

            foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
            {
                var location = Path.Combine(directory, "SKILL.md");
                if (!File.Exists(location))
                    continue;

                var folder = Path.GetFileName(directory);
                var frontmatter = SkillFrontmatter.Parse(File.ReadAllText(location));
                if (frontmatter is null)
                {
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Error,
                        "No YAML frontmatter block — skill skipped."));
                    continue;
                }

                var description = frontmatter.Field("description");
                if (string.IsNullOrWhiteSpace(description))
                {
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Error,
                        "Missing 'description' — skill skipped (a description is what makes it discoverable)."));
                    continue;
                }

                var name = frontmatter.Field("name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Warning,
                        $"Missing 'name' — falling back to the directory name '{folder}'."));
                    name = folder;
                }
                else if (!name.Equals(folder, StringComparison.OrdinalIgnoreCase))
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Warning,
                        $"Name '{name}' does not match the directory name '{folder}'."));

                if (name.Length > MaxNameLength)
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Warning,
                        $"Name is {name.Length} characters, over the {MaxNameLength} allowed by the specification."));

                if (skills.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    diagnostics.Add(new SkillDiagnostic(location, SkillDiagnosticLevel.Warning,
                        $"Another skill is already named '{name}' — this one is shadowed and skipped."));
                    continue;
                }

                skills.Add(new SkillRef(name, description.Trim(), location, directory)
                {
                    Metadata = frontmatter.Metadata,
                });
            }

            return (skills, diagnostics);
        }

        /// <summary>
        /// The mechanical selection behind <c>SKILLS=globs</c> (see <c>GlobSkillSelector</c>): the skills whose
        /// <c>metadata.applies-to</c> globs (comma-separated) match a file of the diff.
        /// <c>applies-to</c> is not part of the specification — it lives under <c>metadata</c> and
        /// is read only here, so a skill without it is never selected this way.
        /// </summary>
        public static IReadOnlyList<SkillRef> MatchByGlobs(
            IEnumerable<SkillRef> catalog, IReadOnlyList<string> files) =>
            catalog.Where(s => Globs(s).Any(g => files.Any(f => Matches(g, f)))).ToList();

        private static string[] Globs(SkillRef skill) =>
            skill.Metadata.TryGetValue("applies-to", out var value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : [];

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

        /// <summary>
        /// Loads the skill's instructions (tier 2). The body is read here, not at discovery, so an
        /// edited <c>SKILL.md</c> takes effect on the next activation.
        /// </summary>
        public static ActivatedSkill Activate(SkillRef skill)
        {
            var frontmatter = SkillFrontmatter.Parse(File.ReadAllText(skill.Location));
            var body = frontmatter?.Body ?? File.ReadAllText(skill.Location).Trim();

            var files = Directory
                .EnumerateFiles(skill.Directory, "*", SearchOption.AllDirectories)
                .Where(f => !string.Equals(f, skill.Location, StringComparison.OrdinalIgnoreCase))
                .Select(f => Path.GetRelativePath(skill.Directory, f).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();

            return new ActivatedSkill(skill.Name, skill.Directory, body, files.Take(MaxResources).ToList())
            {
                Truncated = files.Count > MaxResources,
            };
        }
    }
}
