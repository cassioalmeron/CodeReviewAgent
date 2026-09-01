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
    /// A comparative verdict: which of two reviews (labelled A/B, meaninglessly) is better on a
    /// criterion, or neither. Serialised exactly as the rubric shows it — <c>"A"</c>, <c>"B"</c>,
    /// <c>"tie"</c> — and deserialised tolerant of case, since the model writes what it read.
    /// </summary>
    [JsonConverter(typeof(VerdictJsonConverter))]
    public enum Verdict { A, B, Tie }

    internal sealed class VerdictJsonConverter : JsonConverter<Verdict>
    {
        public override Verdict Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString()?.Trim();
            return value?.ToUpperInvariant() switch
            {
                "A" => Verdict.A,
                "B" => Verdict.B,
                "TIE" => Verdict.Tie,
                _ => throw new JsonException($"Unknown verdict '{value}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, Verdict value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value switch
            {
                Verdict.A => "A",
                Verdict.B => "B",
                Verdict.Tie => "tie",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            });
    }

    /// <summary>
    /// Which prompt version landed in which prompt slot for one judge execution. Kept outside the
    /// prompt — the rubric tells the model the slot carries no meaning — so a verdict of "A" or "B"
    /// can be translated back into a prompt version afterwards.
    /// </summary>
    public record JudgePairing(string SlotA, string SlotB);

    /// <summary>
    /// The judge's comparative verdict for one pair of reviews of the same diff, one criterion at a
    /// time. <see cref="Reasoning"/> is listed first because the model emits properties in schema
    /// order: putting it first is what makes the chain-of-thought real rather than decorative.
    /// </summary>
    public record PairJudgment(
        string? Reasoning,
        Verdict Correctness,
        Verdict Actionability,
        Verdict Calibration,
        [property: JsonPropertyName("signal_to_noise")] Verdict SignalToNoise,
        Verdict Conciseness,
        Verdict Overall);

    /// <summary>A pairwise judgment, the slot assignment it was made under, and the call's cost, latency and token usage.</summary>
    public record PairJudgeOutcome(
        PairJudgment Judgment,
        JudgePairing Pairing,
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

        /// <summary>
        /// Compares two reviews of the same diff — whichever the caller put in <paramref name="a"/>
        /// versus <paramref name="b"/> becomes "Review A" / "Review B" in the prompt; which prompt
        /// version that actually is stays on <see cref="PairJudgeOutcome.Pairing"/>, never in the
        /// prompt itself.
        /// </summary>
        public PairJudgeOutcome Evaluate(string diff, ReviewResult a, ReviewResult b)
        {
            var rubric = LoadRubric(_rubricVersion);
            var userContent =
                $"## Diff\n```diff\n{diff}\n```\n\n" +
                $"## Review A\n{RenderReview(a)}\n\n" +
                $"## Review B\n{RenderReview(b)}";

            var requestBody = new
            {
                max_tokens = 4000,
                system = rubric,
                json_schema = BuildPairSchema(),
                messages = new[]
                {
                    new { role = "user", content = userContent },
                },
            };

            var stopwatch = Stopwatch.StartNew();
            var response = _client.Request(requestBody);
            stopwatch.Stop();

            var content = string.Join("\n", (response?.Content ?? [])
                .Where(block => block.Type == "text")
                .Select(block => block.Text));
            var judgment = ParsePairJudgment(content);

            var inputTokens = response?.Usage?.InputTokens ?? 0;
            var outputTokens = response?.Usage?.OutputTokens ?? 0;
            var cost = CostCalculator.Estimate("claude", response?.Model, inputTokens, outputTokens);

            var pairing = new JudgePairing(a.PromptVersion ?? "?", b.PromptVersion ?? "?");
            return new PairJudgeOutcome(judgment, pairing, cost, stopwatch.ElapsedMilliseconds, inputTokens, outputTokens);
        }

        private static string RenderReview(ReviewResult review)
        {
            var findings = JsonSerializer.Serialize(review.Findings ?? [], FindingsJsonOptions);
            return $"Summary: {review.Summary}\n\nFindings:\n```json\n{findings}\n```";
        }

        // JSON schema for the pairwise judgment: reasoning first (in both properties and required),
        // so schema order — not instruction — is what forces the chain-of-thought before the verdict.
        private static object BuildPairSchema()
        {
            static object VerdictField() => new { type = "string", @enum = new[] { "A", "B", "tie" } };
            return new
            {
                type = "object",
                properties = new
                {
                    reasoning = new { type = "string" },
                    correctness = VerdictField(),
                    actionability = VerdictField(),
                    calibration = VerdictField(),
                    signal_to_noise = VerdictField(),
                    conciseness = VerdictField(),
                    overall = VerdictField(),
                },
                required = new[]
                {
                    "reasoning", "correctness", "actionability", "calibration",
                    "signal_to_noise", "conciseness", "overall",
                },
                additionalProperties = false,
            };
        }

        // A verdict that fails to parse degrades to an all-tie judgment rather than aborting the
        // whole run over one bad response — the same resilience the absolute path already has, and
        // with judgeRuns executions per pair the noise of one bad call is meant to average out.
        private static PairJudgment ParsePairJudgment(string content)
        {
            try
            {
                return JsonSerializer.Deserialize<PairJudgment>(content, JsonOptions)
                    ?? UnreadablePairJudgment;
            }
            catch (JsonException)
            {
                return UnreadablePairJudgment;
            }
        }

        private static readonly PairJudgment UnreadablePairJudgment = new(
            null, Verdict.Tie, Verdict.Tie, Verdict.Tie, Verdict.Tie, Verdict.Tie, Verdict.Tie);

        private static string LoadRubric(string version)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "rubrics", $"judge-{version}.md");
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
