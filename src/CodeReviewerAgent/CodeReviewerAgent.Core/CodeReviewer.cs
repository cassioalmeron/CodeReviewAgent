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
        private readonly IDiffSource _diffSource;

        public CodeReviewer(ILlmClient client, IDiffSource diffSource)
        {
            _client = client;
            _diffSource = diffSource;
        }

        public ReviewResult Review()
        {
            // Read the diff from the configured source
            var diff = _diffSource.GetDiff();
            if (string.IsNullOrWhiteSpace(diff))
            {
                System.Console.WriteLine("No changes to review.");
                return new ReviewResult(null, []);
            }

            // Send the diff to the LLM, using the versioned system prompt and a JSON
            // schema so the model returns structured output (summary + findings).
            var promptVersion = Environment.GetEnvironmentVariable("PROMPT_VERSION") ?? "v1";
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
            var findings = result.Findings ?? [];

            if (!string.IsNullOrWhiteSpace(result.Summary))
                System.Console.WriteLine(result.Summary);
            DisplayFindings(findings);

            var inputTokens = response?.Usage?.InputTokens ?? 0;
            var outputTokens = response?.Usage?.OutputTokens ?? 0;
            var engine = Environment.GetEnvironmentVariable("LLM_ENGINE");
            var cost = CostCalculator.Estimate(engine, response?.Model, inputTokens, outputTokens);
            Logger.Log(new
            {
                timestamp = DateTime.UtcNow,
                engine,
                model = response?.Model,
                prompt_version = promptVersion,
                input_tokens = inputTokens,
                output_tokens = outputTokens,
                total_tokens = inputTokens + outputTokens,
                cost_usd = cost,
                latency_ms = stopwatch.ElapsedMilliseconds,
                findings_count = findings.Count,
            });

            // Write the structured review to a file
            WriteReviewToFile(content);

            return result with { Findings = findings };
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
                            line = new { type = "integer" },
                            severity = new { type = "string", @enum = new[] { "info", "warning", "critical" } },
                            category = new { type = "string", @enum = new[] { "bug", "security", "performance", "style", "maintainability" } },
                            problem = new { type = "string" },
                            suggestion = new { type = "string" },
                        },
                        required = new[] { "file", "line", "severity", "category", "problem", "suggestion" },
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

        private static void WriteReviewToFile(string content)
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "reviews");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"review-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.json");
            File.WriteAllText(path, content);
            System.Console.WriteLine($"Review saved to {path}");
        }
    }
}
