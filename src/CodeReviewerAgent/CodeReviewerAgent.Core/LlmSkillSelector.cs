using System.Text.Json;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// Model-driven activation: a small, schema-constrained call that receives the catalog and
    /// the files of the diff and answers with the skill names that apply. The <c>enum</c> in the
    /// schema pins the answer to existing names; anything else the model invents is dropped here.
    /// <para>
    /// There is no fallback: an answer that can't be read selects nothing, flagged as
    /// <see cref="SkillSelection.Unreadable"/>. Substituting another strategy's decision would make
    /// a broken selection indistinguishable from a working one — the measurement would report the
    /// substitute's accuracy as if it were the model's. The globs remain available deliberately,
    /// as <c>SKILLS=globs</c>.
    /// </para>
    /// </summary>
    public sealed class LlmSkillSelector(ILlmClient client) : ISkillSelector
    {
        // The answer itself is a dozen tokens, but models that reason before answering spend the
        // budget on the way there — and a truncated response is an unreadable one. A run pegged at
        // exactly this number in the trigger eval's cost table means it was cut off, not that it
        // needed this much.
        private const int MaxTokens = 300;

        public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files)
        {
            var names = catalog.Select(s => s.Name).ToArray();
            var requestBody = new
            {
                max_tokens = MaxTokens,
                system = SkillPrompt.Selection(catalog, SkillPrompt.Version),
                json_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        skills = new
                        {
                            type = "array",
                            items = new { type = "string", @enum = names },
                        },
                    },
                    required = new[] { "skills" },
                    additionalProperties = false,
                },
                messages = new[]
                {
                    new { role = "user", content = $"Files changed by the diff:\n{string.Join("\n", files)}" },
                },
            };

            var response = client.Request(requestBody);
            var content = string.Join("\n", (response?.Content ?? [])
                .Where(b => b.Type == "text")
                .Select(b => b.Text));

            // Accounted before parsing: an unreadable answer was paid for just the same, and those
            // are the runs that go wrong most often — reporting them as free understates the cost
            // of exactly what needs measuring.
            var inputTokens = response?.Usage?.InputTokens ?? 0;
            var outputTokens = response?.Usage?.OutputTokens ?? 0;
            var engine = Environment.GetEnvironmentVariable("LLM_ENGINE");
            var cost = response?.Cost
                ?? CostCalculator.Estimate(engine, response?.Model, inputTokens, outputTokens);

            var selected = Parse(content, names);
            if (selected is null)
            {
                // The answer itself, not just the verdict: without seeing what came back there is
                // no way to tell prose from a truncated payload from an unexpected shape.
                System.Console.WriteLine(
                    $"Skill selection unreadable — no skills loaded for this review. Got: {Preview(content)}");
                return new SkillSelection([], inputTokens, outputTokens, cost) { Unreadable = true };
            }

            return new SkillSelection(selected, inputTokens, outputTokens, cost);
        }

        private const int PreviewLength = 200;

        /// <summary>One-line, bounded rendering of a response, for the unreadable-answer message.</summary>
        private static string Preview(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return "(empty response)";

            var single = content.ReplaceLineEndings(" ").Trim();
            return single.Length <= PreviewLength ? single : $"{single[..PreviewLength]}… (+{single.Length - PreviewLength} chars)";
        }

        /// <summary>
        /// Reads the chosen names, keeping only the ones present in the catalog. Returns null when
        /// the answer isn't a readable selection at all — no skills are loaded then.
        /// <para>
        /// The schema asks for <c>{ "skills": [...] }</c>, but engines without native schema
        /// enforcement answer in every neighbouring shape: the bare array, the singular key, names
        /// wrapped in objects. All of them are read. So is every way of saying "nothing" — an empty
        /// body, <c>null</c>, <c>{}</c>, a null-valued key — because that is the correct answer for
        /// most diffs, and scoring it as a failure would bury the negative cases.
        /// </para>
        /// </summary>
        private static IReadOnlyList<string>? Parse(string content, string[] names)
        {
            if (string.IsNullOrWhiteSpace(content))
                return [];

            try
            {
                using var document = JsonDocument.Parse(content);
                var root = document.RootElement;

                // TryGetProperty throws on anything that isn't an object, so guard the kind first.
                if (root.ValueKind == JsonValueKind.Object
                    && (ArrayProperty(root, "skills") ?? ArrayProperty(root, "skill")) is { } wrapped)
                    return Read(wrapped, names);

                return root.ValueKind switch
                {
                    JsonValueKind.Array => Read(root, names),
                    JsonValueKind.Null => [],
                    // `{}` or `{ "skills": null }` say "nothing"; an object of some other shape is
                    // an answer to a different question, and cannot be read as a selection.
                    JsonValueKind.Object when SaysNothing(root) => [],
                    _ => null,
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static IReadOnlyList<string>? Read(JsonElement array, string[] names)
        {
            var items = array.EnumerateArray().ToList();
            var chosen = items.Select(NameOf).OfType<string>().ToList();

            // A non-empty array from which no name could be read is not a selection.
            if (items.Count > 0 && chosen.Count == 0)
                return null;

            return chosen
                .Where(name => names.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool SaysNothing(JsonElement element) =>
            !element.EnumerateObject().Any()
            || IsNull(element, "skills")
            || IsNull(element, "skill");

        private static bool IsNull(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Null;

        /// <summary>
        /// The skill name of one array element. Models answer with plain strings
        /// (<c>["csharp"]</c>) or with objects wrapping the name
        /// (<c>[{"name": "csharp"}]</c>); both say the same thing, and reading the second is
        /// better than discarding a real choice and loading nothing.
        /// </summary>
        private static string? NameOf(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Object => Property(element, "name") ?? Property(element, "skill"),
            _ => null,
        };

        /// <summary>
        /// The array held by <paramref name="name"/>, or null. Both the schema's <c>skills</c> and
        /// the singular <c>skill</c> are accepted: the key differs, the intent does not.
        /// </summary>
        private static JsonElement? ArrayProperty(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
                ? value
                : null;

        private static string? Property(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
