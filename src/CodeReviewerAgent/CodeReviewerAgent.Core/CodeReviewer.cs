using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    public class CodeReviewer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly ILlmClient _client;
        private readonly string _diff;

        public CodeReviewer(ILlmClient client, string diff)
        {
            _client = client;
            _diff = diff;
        }

        /// <summary>
        /// Runs a review and writes its Markdown report, returning the result. Keeps
        /// report generation out of the core <see cref="Review"/> flow.
        /// </summary>
        public static ReviewResult ReviewAndReport(ILlmClient client, string diff)
        {
            var result = new CodeReviewer(client, diff).Review();
            var reportPath = ReportGenerator.Save(result);
            System.Console.WriteLine($"Report saved to {reportPath}");
            return result;
        }

        public ReviewResult Review()
        {
            // Drop Markdown files: they are prose, not code we want reviewed.
            var diff = DiffFilter.ExcludeMarkdown(_diff);
            if (string.IsNullOrWhiteSpace(diff))
            {
                System.Console.WriteLine("No changes to review.");
                return new ReviewResult(null, []);
            }

            // Send the diff to the LLM, using the versioned system prompt and a JSON
            // schema so the model returns structured output (summary + findings).
            var promptVersion = Environment.GetEnvironmentVariable("PROMPT_VERSION") ?? "v2";
            var systemPrompt = LoadSystemPrompt(promptVersion);
            var requestBody = new
            {
                max_tokens = 16000,
                system = systemPrompt,
                json_schema = BuildSchema(),
                messages = new[]
                {
                    new { role = "user", content = $"```diff\n{diff}\n```" },
                },
            };

            var stopwatch = Stopwatch.StartNew();
            var response = _client.Request(requestBody);
            stopwatch.Stop();

            // The structured response is a JSON object: { "summary": ..., "findings": [...] }
            var content = string.Join("\n", (response?.Content ?? [])
                .Where(b => b.Type == "text")
                .Select(b => b.Text));

            var result = ParseResult(content);
            var rawFindings = result.Findings ?? [];

            // Ground each finding against the parsed diff: keep only those whose cited
            // snippet matches an added line, deriving the real line number from it.
            var parsedDiff = DiffParser.Parse(diff);
            var findings = FindingValidator.Validate(rawFindings, parsedDiff);

            if (!string.IsNullOrWhiteSpace(result.Summary))
                System.Console.WriteLine(result.Summary);
            DisplayFindings(findings);

            var inputTokens = response?.Usage?.InputTokens ?? 0;
            var outputTokens = response?.Usage?.OutputTokens ?? 0;
            var engine = Environment.GetEnvironmentVariable("LLM_ENGINE");
            var cost = response?.Cost
                ?? CostCalculator.Estimate(engine, response?.Model, inputTokens, outputTokens);

            // Return the validated review (with derived line numbers). Persistence is a
            // separate step: the caller stores it through the repository.
            return result with
            {
                Findings = findings,
                Engine = engine,
                Model = response?.Model,
                PromptVersion = promptVersion,
                Cost = cost,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                Diff = diff,
            };
        }

        private static string LoadSystemPrompt(string version)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "prompts", $"review-{version}.md");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Review prompt not found: {path}");
            return File.ReadAllText(path);
        }

        // JSON schema for the structured review output (summary + findings).
        private static object BuildSchema() => new
        {
            type = "object",
            properties = new
            {
                summary = new { type = "string" },
                findings = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            file = new { type = "string" },
                            code_snippet = new { type = "string" },
                            severity = new { type = "string", @enum = new[] { "info", "warning", "critical" } },
                            category = new { type = "string", @enum = new[] { "bug", "security", "performance", "style", "maintainability" } },
                            problem = new { type = "string" },
                            suggestion = new { type = "string" },
                        },
                        required = new[] { "file", "code_snippet", "severity", "category", "problem", "suggestion" },
                        additionalProperties = false,
                    },
                },
            },
            required = new[] { "summary", "findings" },
            additionalProperties = false,
        };

        private static ReviewResult ParseResult(string content)
        {
            try
            {
                return JsonSerializer.Deserialize<ReviewResult>(content, JsonOptions)
                    ?? new ReviewResult(null, null);
            }
            catch (JsonException)
            {
                return new ReviewResult(null, null);
            }
        }

        private static void DisplayFindings(List<Finding> findings)
        {
            System.Console.WriteLine();
            System.Console.WriteLine($"Findings: {findings.Count}");
            foreach (var f in findings)
                System.Console.WriteLine(
                    $"  [{f.Severity}] {f.File}:{f.Line} ({f.Category}) — {f.Problem} -> {f.Suggestion}");
        }

    }
}
