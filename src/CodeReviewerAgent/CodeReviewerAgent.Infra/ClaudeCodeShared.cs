using System.Text.Json;

namespace CodeReviewerAgent.Infra
{
    /// <summary>
    /// Logic shared by the Claude Code clients (SDK-based and direct-CLI). Both drive the
    /// local Claude CLI, which has no native JSON-schema enforcement, so the schema is
    /// injected into the system prompt and the response is parsed as JSON text.
    /// </summary>
    internal static class ClaudeCodeShared
    {
        /// <summary>
        /// Extracts the system prompt and the diff from the Anthropic-shaped request body.
        /// Unlike the API, the CLI can't enforce a JSON schema, so JSON output is coerced by
        /// prompting: a forceful directive (plus the schema) is folded into the system prompt
        /// and a final reminder is appended to the user message. Without this the model
        /// tends to answer with prose, which the review parser can't read.
        /// </summary>
        public static (string SystemPrompt, string Diff) BuildRequest(object requestBody)
        {
            var body = JsonSerializer.SerializeToElement(requestBody);
            var system = body.TryGetProperty("system", out var s) ? s.GetString() : null;
            var diff = body.GetProperty("messages")[0].GetProperty("content").GetString() ?? string.Empty;
            var schema = body.GetProperty("json_schema").GetRawText();

            var systemPrompt =
                $"{system}\n\n" +
                "CRITICAL OUTPUT FORMAT — this overrides any output format implied above. Your " +
                "entire response MUST be ONE raw JSON object and nothing else: no prose, no " +
                "headings, no markdown, and no code fences. The first character MUST be '{' and " +
                "the last MUST be '}'. The object MUST conform EXACTLY to this JSON schema — use " +
                "only the property names it defines, with those exact names and exact enum " +
                "values, and include no other properties (for example, do not invent keys like " +
                "\"line\", \"verdict\", or \"failure_scenario\"):\n" +
                schema;

            var userPrompt = diff +
                "\n\nRemember: respond with ONLY the raw JSON object conforming to that schema — " +
                "exact keys, no extra keys, no prose, starting with '{' and ending with '}'.";

            return (systemPrompt, userPrompt);
        }

        /// <summary>
        /// The prompt forbids code fences, but strip them defensively so the review parser
        /// never chokes on a ```json ... ``` wrapper.
        /// </summary>
        public static string StripFences(string s)
        {
            s = s.Trim();
            if (!s.StartsWith("```"))
                return s;

            var start = s.IndexOf('\n');
            var end = s.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
                s = s[(start + 1)..end];
            return s.Trim();
        }
    }
}
