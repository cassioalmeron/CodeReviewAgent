namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The parsed head of a <c>SKILL.md</c>: the top-level frontmatter fields, the nested
    /// <c>metadata:</c> map, and the markdown body after the closing <c>---</c>.
    /// <para>
    /// A line-based reader, not a YAML engine: the Agent Skills frontmatter is a handful of
    /// scalars (<c>name</c>, <c>description</c>, <c>license</c>, <c>allowed-tools</c>) plus an
    /// open <c>metadata</c> map. Splitting on the first colon makes the reader lenient by
    /// construction — an unquoted colon inside a description ("Use when: ...") parses fine,
    /// which is the compatibility fallback the specification asks for.
    /// </para>
    /// </summary>
    internal sealed record SkillFrontmatter(
        IReadOnlyDictionary<string, string> Fields,
        IReadOnlyDictionary<string, string> Metadata,
        string Body)
    {
        public string? Field(string key) => Fields.TryGetValue(key, out var value) ? value : null;

        /// <summary>Parses <paramref name="text"/>, or returns null when there is no frontmatter block.</summary>
        public static SkillFrontmatter? Parse(string text)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---")
                return null;

            var end = Array.FindIndex(lines, 1, line => line.Trim() == "---");
            if (end < 0)
                return null;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string>? nested = null;

            for (var i = 1; i < end; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    continue;

                var colon = line.IndexOf(':');
                if (colon < 0)
                    continue;

                var indent = line.Length - line.TrimStart().Length;
                var key = line[..colon].Trim();
                var value = Unquote(line[(colon + 1)..].Trim());

                // An indented line belongs to the map opened by the last top-level key; a
                // top-level key closes whatever map was open.
                var target = indent > 0 ? nested : fields;
                if (indent == 0)
                    nested = null;

                if (value is "|" or "|-" or ">" or ">-")
                    value = ReadBlockScalar(lines, ref i, end, indent, folded: value[0] == '>');
                else if (value.Length == 0 && indent == 0)
                {
                    // A key with no value opens a nested map; only `metadata` is kept.
                    nested = key.Equals("metadata", StringComparison.OrdinalIgnoreCase) ? metadata : [];
                    continue;
                }

                if (target is not null)
                    target[key] = value;
            }

            return new SkillFrontmatter(fields, metadata, string.Join("\n", lines[(end + 1)..]).Trim());
        }

        // Collects the lines indented deeper than the key that introduced the block scalar.
        private static string ReadBlockScalar(string[] lines, ref int i, int end, int parentIndent, bool folded)
        {
            var collected = new List<string>();
            while (i + 1 < end)
            {
                var next = lines[i + 1];
                if (!string.IsNullOrWhiteSpace(next) && next.Length - next.TrimStart().Length <= parentIndent)
                    break;
                collected.Add(next.Trim());
                i++;
            }
            return string.Join(folded ? " " : "\n", collected).Trim();
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                return value[1..^1];
            return value;
        }
    }
}
