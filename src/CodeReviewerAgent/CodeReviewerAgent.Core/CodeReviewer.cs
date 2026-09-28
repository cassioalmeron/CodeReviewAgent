using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core.Diff;
using CodeReviewerAgent.Core.Llm;
using CodeReviewerAgent.Core.Skill;

namespace CodeReviewerAgent.Core;

public class CodeReviewer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ILlmClient _client;
    private readonly string _diff;
    private readonly ReviewSettings _settings;
    private readonly ISkillSelector _skillSelector;
    private readonly ISkillSource _skills;
    private readonly IProgress<string>? _progress;

    /// <param name="settings">
    /// Prompt version, engine and skill strategy. These used to be read from the environment
    /// inside this class and its collaborators (US-018).
    /// </param>
    /// <param name="skills">Where the skills come from; the files under <c>assets/skills/</c> by default.</param>
    /// <param name="progress">
    /// Where a notice that only makes sense mid-flow goes, such as a diff with nothing left to
    /// review. Everything the caller can see in the returned result is left for the caller to
    /// report; null stays silent, which is what the golden set's parallel rounds want.
    /// </param>
    public CodeReviewer(
        ILlmClient client, string diff, ReviewSettings settings,
        ISkillSelector? skillSelector = null, ISkillSource? skills = null,
        IProgress<string>? progress = null)
    {
        _client = client;
        _diff = diff;
        _settings = settings;
        _skillSelector = skillSelector ?? SkillSelectorFactory.Create(
            client, settings.Skills, settings.SkillPromptVersion, settings.Engine, progress);
        _skills = skills ?? new FileSkillSource();
        _progress = progress;
    }

    public ReviewResult Review()
    {
        // Drop Markdown files: they are prose, not code we want reviewed.
        var diff = DiffFilter.ExcludeMarkdown(_diff);
        if (string.IsNullOrWhiteSpace(diff))
        {
            _progress?.Report("No changes to review.");
            return new ReviewResult(null, []);
        }

        // Send the diff to the LLM, using the versioned system prompt and a JSON
        // schema so the model returns structured output (summary + findings).
        var systemPrompt = LoadSystemPrompt(_settings.PromptVersion);

        // The clock covers both LLM calls: the selection below and the review itself.
        var stopwatch = Stopwatch.StartNew();

        // Append the project guidelines ("skills") the model picked for this diff, so it also
        // reports convention violations as findings.
        var files = DiffSplitter.ByFile(diff).Select(f => f.Path).Distinct().ToList();
        var skills = ActivateSkills(files);
        systemPrompt += SkillPrompt.Guidelines(skills.Skills, _settings.SkillPromptVersion);

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

        // The selection call is part of the review: its tokens and cost are folded into the
        // totals, so an assessment never reports less than the run actually spent.
        var inputTokens = (response?.Usage?.InputTokens ?? 0) + skills.InputTokens;
        var outputTokens = (response?.Usage?.OutputTokens ?? 0) + skills.OutputTokens;
        var cost = (response?.Cost
            ?? CostCalculator.Estimate(_settings.Engine, response?.Model,
                response?.Usage?.InputTokens ?? 0, response?.Usage?.OutputTokens ?? 0))
            + skills.Cost;

        // Return the validated review (with derived line numbers). Persistence is a
        // separate step: the caller stores it through the repository.
        return result with
        {
            Findings = findings,
            // What the grounding threw away. It is the only trace left of a finding that was about
            // the right problem but cited a line the diff does not add.
            DiscardedFindings = rawFindings.Count - findings.Count,
            Engine = _settings.Engine,
            Model = response?.Model,
            PromptVersion = _settings.PromptVersion,
            Skills = skills.Names,
            Cost = cost,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            Diff = diff,
        };
    }

    /// <summary>What the skill flow contributed to a run: the activated skills and their cost.</summary>
    private sealed record SkillActivation(
        IReadOnlyList<ActivatedSkill> Skills, int InputTokens, int OutputTokens, decimal Cost)
    {
        public static readonly SkillActivation None = new([], 0, 0, 0m);

        /// <summary>The activated names for the persisted assessment; null when none were.</summary>
        public string? Names =>
            Skills.Count == 0 ? null : string.Join(",", Skills.Select(s => s.Name));
    }

    /// <summary>
    /// Progressive disclosure, tier 1 → tier 2: the catalog is offered to the selector, and
    /// only the skills it chose have their instructions loaded. How the choice was made —
    /// the model, the globs, the user — belongs to the strategy, not here.
    /// </summary>
    private SkillActivation ActivateSkills(IReadOnlyList<string> files)
    {
        var catalog = _skills.Catalog();
        if (catalog.Count == 0 || files.Count == 0)
            return SkillActivation.None;

        var selection = _skillSelector.Select(catalog, files);
        var activated = catalog
            .Where(s => selection.Names.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(_skills.Activate)
            .ToList();

        return new SkillActivation(
            activated, selection.InputTokens, selection.OutputTokens, selection.Cost);
    }

    private static string LoadSystemPrompt(string version)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "prompts", $"review-{version}.md");
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
                        category = new { type = "string", @enum = new[] { "bug", "security", "performance", "style", "maintainability", "convention" } },
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

}
