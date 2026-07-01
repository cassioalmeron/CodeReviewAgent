using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The judge's quality scores for one review (each 1-5), plus a short rationale.
    /// </summary>
    public record Judgment(
        int Correctness,
        int Actionability,
        int Calibration,
        [property: JsonPropertyName("signal_to_noise")] int SignalToNoise,
        int Overall,
        string? Rationale);

    /// <summary>A judgment plus the judge call's cost, latency and token usage.</summary>
    public record JudgeOutcome(
        Judgment Judgment,
        decimal Cost,
        long LatencyMs,
        int InputTokens,
        int OutputTokens);

    /// <summary>
    /// LLM-as-judge: scores the quality of an agent's review against a versioned rubric,
    /// using a (stronger) model than the executor to avoid self-preference bias. The
    /// inferential evaluation layer — it measures comment quality, not detection.
    /// </summary>
    public class Judge
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly JsonSerializerOptions FindingsJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        private readonly ILlmClient _client;
        private readonly string _rubricVersion;

        public Judge(ILlmClient client, string rubricVersion)
        {
            _client = client;
            _rubricVersion = rubricVersion;
        }

        public JudgeOutcome Evaluate(string diff, ReviewResult review)
        {
            var rubric = LoadRubric(_rubricVersion);
            var findings = JsonSerializer.Serialize(review.Findings ?? [], FindingsJsonOptions);
            var userContent =
                $"## Diff\n```diff\n{diff}\n```\n\n" +
                $"## Agent summary\n{review.Summary}\n\n" +
                $"## Agent findings\n```json\n{findings}\n```";

            var requestBody = new
            {
                max_tokens = 4000,
                system = rubric,
                json_schema = BuildSchema(),
                messages = new[]
                {
                    new { role = "user", content = userContent },
                },
            };

            var stopwatch = Stopwatch.StartNew();
            var response = _client.Request(requestBody);
            stopwatch.Stop();

            var content = string.Join("\n", (response?.Content ?? [])
                .Where(b => b.Type == "text")
                .Select(b => b.Text));
            var judgment = ParseJudgment(content);

            var inputTokens = response?.Usage?.InputTokens ?? 0;
            var outputTokens = response?.Usage?.OutputTokens ?? 0;
            var cost = CostCalculator.Estimate("claude", response?.Model, inputTokens, outputTokens);

            return new JudgeOutcome(judgment, cost, stopwatch.ElapsedMilliseconds, inputTokens, outputTokens);
        }

        private static string LoadRubric(string version)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "rubrics", $"judge-{version}.md");
            if (!File.Exists(path))
                throw new FileNotFoundException($"Judge rubric not found: {path}");
            return File.ReadAllText(path);
        }

        // JSON schema for the structured judgment (four criteria + overall + rationale).
        private static object BuildSchema()
        {
            // The 1-5 range is enforced by the rubric prompt; Claude's structured output
            // does not allow minimum/maximum on integer types.
            object Score() => new { type = "integer" };
            return new
            {
                type = "object",
                properties = new
                {
                    correctness = Score(),
                    actionability = Score(),
                    calibration = Score(),
                    signal_to_noise = Score(),
                    overall = Score(),
                    rationale = new { type = "string" },
                },
                required = new[]
                {
                    "correctness", "actionability", "calibration", "signal_to_noise", "overall", "rationale",
                },
                additionalProperties = false,
            };
        }

        private static Judgment ParseJudgment(string content)
        {
            try
            {
                return JsonSerializer.Deserialize<Judgment>(content, JsonOptions)
                    ?? new Judgment(0, 0, 0, 0, 0, null);
            }
            catch (JsonException)
            {
                return new Judgment(0, 0, 0, 0, 0, null);
            }
        }
    }
}
