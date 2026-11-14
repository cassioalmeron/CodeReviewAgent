using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core.Advisor;
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

    private const string AdvisorPrompt = "advisor-v1";
    private const string AdvisorHintsPrompt = "advisor-hints-v1";

    // Out of the 16000-token budget this leaves 4000 for the answer, so a model that reasons long still replies.
    private const int AdvisorReasoningMaxTokens = 12000;

    private readonly ILlmClient _client;
    private readonly ILlmClient? _advisor;
    private readonly string _diff;
    private readonly ReviewSettings _settings;
    private readonly ISkillSelector _skillSelector;
    private readonly ISkillSource _skills;
    private readonly IProgress<string>? _progress;

    /// <param name="settings">
    /// Prompt version, engine and skill strategy. These used to be read from the environment
    /// inside this class and its collaborators.
    /// </param>
    /// <param name="skills">Where the skills come from; the files under <c>assets/skills/</c> by default.</param>
    /// <param name="progress">
    /// Where a notice that only makes sense mid-flow goes, such as a diff with nothing left to
    /// review. Everything the caller can see in the returned result is left for the caller to
    /// report; null stays silent, which is what the golden set's parallel rounds want.
    /// </param>
    /// <param name="advisor">
    /// The stronger model that tells the second review where to look. Required, and used, only under
    /// the advisor pattern.
    /// </param>
    public CodeReviewer(
        ILlmClient client, string diff, ReviewSettings settings,
        ISkillSelector? skillSelector = null, ISkillSource? skills = null,
        IProgress<string>? progress = null, ILlmClient? advisor = null)
    {
        _client = client;
        _advisor = advisor;
        _diff = diff;
        _settings = settings;
        if (!ReviewSettings.IsKnownPattern(settings.Pattern))
            throw new ArgumentException(
                $"Unknown review pattern '{settings.Pattern}'; expected '{ReviewSettings.SinglePattern}', '{ReviewSettings.VotingPattern}' or '{ReviewSettings.AdvisorPattern}'.",
                nameof(settings));
        if (IsAdvisor && advisor is null)
            throw new ArgumentException(
                $"The '{ReviewSettings.AdvisorPattern}' review pattern needs an advisor client.", nameof(advisor));
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

        // The clock covers every LLM call: the selection below and the review itself. With the
        // voting pattern the review is the parallel block, so the clock reads its wall time.
        var stopwatch = Stopwatch.StartNew();

        // Selected once and shared: the same diff gives the same selection, and repeating it per
        // review would pay for it again whenever the model is the one choosing.
        var files = DiffSplitter.ByFile(diff).Select(f => f.Path).Distinct().ToList();
        var skills = ActivateSkills(files);
        var guidelines = SkillPrompt.Guidelines(skills.Skills, _settings.SkillPromptVersion);

        var parsedDiff = DiffParser.Parse(diff);

        var merged = IsVoting
            ? ReviewByVoting(diff, parsedDiff, guidelines)
            : ReviewOnce(_settings.PromptVersion, diff, parsedDiff, guidelines, null);
        stopwatch.Stop();

        // The selection call is part of the review: its tokens and cost are folded into the
        // totals, so an assessment never reports less than the run actually spent.
        // Return the validated review (with derived line numbers). Persistence is a
        // separate step: the caller stores it through the repository.
        var first = merged.Result with
        {
            Engine = _settings.Engine,
            Model = merged.Model,
            PromptVersion = _settings.PromptVersion,
            Skills = skills.Names,
            Cost = merged.Cost + skills.Cost,
            LatencyMs = stopwatch.ElapsedMilliseconds,
            InputTokens = merged.InputTokens + skills.InputTokens,
            OutputTokens = merged.OutputTokens + skills.OutputTokens,
            Diff = diff,
        };

        if (!IsAdvisor)
            return first;

        return AdvisorTrigger.ShouldEscalate(first.Findings ?? [])
            ? Escalate(first)
            : first with { Advisor = new AdvisorTrace(false, null, 0m, 0, 0, 0) };
    }

    /// <summary>
    /// Always escalates; whether to is the caller's decision. The advisor reads the diff and the
    /// findings of <paramref name="first"/> and says where to look, then the executor reviews again
    /// under the same system prompt, with the hints after the diff. That second review is the final
    /// one: its findings replace the first's, and the cost, tokens and latency cover all three calls.
    /// When the advisor gives no hint, or its reply cannot be read (the trace is then marked failed),
    /// there is no second review: the first review stands, and the totals cover the first review and
    /// the advisor.
    /// </summary>
    public ReviewResult Escalate(ReviewResult first)
    {
        if (_advisor is null)
            throw new InvalidOperationException(
                $"Escalating needs an advisor client, and none was given to this {nameof(CodeReviewer)}.");

        var stopwatch = Stopwatch.StartNew();
        var diff = first.Diff ?? DiffFilter.ExcludeMarkdown(_diff);
        var advice = Advise(_advisor, diff, first.Findings ?? []);

        // With nothing to look at again, a second review is the first one repeated: the first review stands.
        if (advice.Hints.Count == 0)
        {
            stopwatch.Stop();
            return first with
            {
                Cost = first.Cost + advice.Trace.Cost,
                LatencyMs = first.LatencyMs + stopwatch.ElapsedMilliseconds,
                InputTokens = first.InputTokens + advice.Trace.InputTokens,
                OutputTokens = first.OutputTokens + advice.Trace.OutputTokens,
                Diff = diff,
                Advisor = advice.Trace,
            };
        }

        var second = ReviewOnce(
            _settings.PromptVersion, diff, DiffParser.Parse(diff), GuidelinesOf(first.Skills), null,
            HintsBlock(advice.Hints));
        stopwatch.Stop();

        return second.Result with
        {
            Engine = first.Engine,
            Model = second.Model ?? first.Model,
            PromptVersion = first.PromptVersion,
            Skills = first.Skills,
            Cost = first.Cost + advice.Trace.Cost + second.Cost,
            LatencyMs = first.LatencyMs + stopwatch.ElapsedMilliseconds,
            InputTokens = first.InputTokens + advice.Trace.InputTokens + second.InputTokens,
            OutputTokens = first.OutputTokens + advice.Trace.OutputTokens + second.OutputTokens,
            Diff = diff,
            Advisor = advice.Trace,
        };
    }

    private bool IsVoting =>
        string.Equals(_settings.Pattern, ReviewSettings.VotingPattern, StringComparison.OrdinalIgnoreCase);

    private bool IsAdvisor =>
        string.Equals(_settings.Pattern, ReviewSettings.AdvisorPattern, StringComparison.OrdinalIgnoreCase);

    /// <summary>What one call to the model produced, grounded and with its own cost.</summary>
    private sealed record ReviewPass(
        ReviewResult Result, string? Model, decimal Cost, int InputTokens, int OutputTokens);

    /// <summary>
    /// One call to the model with the prompt of <paramref name="promptVersion"/>. When
    /// <paramref name="scope"/> is given, a finding whose category is outside it is dropped before
    /// the grounding and counted apart, so <see cref="ReviewResult.DiscardedFindings"/> keeps
    /// meaning "the cited line is not in the diff". <paramref name="extraUserContent"/> goes in the
    /// user message after the diff, so the system prompt stays the one the first review sent.
    /// </summary>
    private ReviewPass ReviewOnce(
        string promptVersion, string diff, ParsedDiff parsedDiff, string guidelines, IReadOnlyList<Category>? scope,
        string? extraUserContent = null)
    {
        // Send the diff to the LLM, using the versioned system prompt and a JSON
        // schema so the model returns structured output (summary + findings).
        // The project guidelines ("skills") the model picked for this diff are appended, so it also
        // reports convention violations as findings.
        var requestBody = new
        {
            max_tokens = 16000,
            system = $"{LoadPrompt($"review-{promptVersion}")}{guidelines}",
            json_schema = BuildSchema(),
            messages = new[]
            {
                new { role = "user", content = DiffBlock(diff, extraUserContent) },
            },
        };

        var response = _client.Request(requestBody);

        // The structured response is a JSON object: { "summary": ..., "findings": [...] }
        var content = string.Join("\n", (response?.Content ?? [])
            .Where(b => b.Type == "text")
            .Select(b => b.Text));

        var result = ParseResult(content);
        var rawFindings = result.Findings ?? [];
        var inScope = scope is null ? rawFindings : rawFindings.Where(f => scope.Any(c => c == f.Category)).ToList();

        // Ground each finding against the parsed diff: keep only those whose cited
        // snippet matches an added line, deriving the real line number from it.
        var findings = FindingValidator.Validate(inScope, parsedDiff);

        var inputTokens = response?.Usage?.InputTokens ?? 0;
        var outputTokens = response?.Usage?.OutputTokens ?? 0;
        var cost = response?.Cost ?? CostCalculator.Estimate(_settings.Engine, response?.Model, inputTokens, outputTokens);

        return new ReviewPass(
            result with
            {
                Findings = findings,
                // What the grounding threw away. It is the only trace left of a finding that was about
                // the right problem but cited a line the diff does not add.
                DiscardedFindings = inScope.Count - findings.Count,
                OutOfScopeFindings = rawFindings.Count - inScope.Count,
            },
            response?.Model, cost, inputTokens, outputTokens);
    }

    /// <summary>
    /// Four reviews at once, one per subject, each answering only for its own categories. The
    /// findings are concatenated: two reviews citing the same line both stay, since each is the only
    /// one looking at its subject and they are not meant to agree. Cost and tokens are sums. The
    /// summary keeps every review's own text, labelled by subject, one per line.
    /// </summary>
    private ReviewPass ReviewByVoting(string diff, ParsedDiff parsedDiff, string guidelines)
    {
        var passes = new ReviewPass[VotingReviews.Length];
        Parallel.For(0, passes.Length, i =>
            passes[i] = ReviewOnce(
                $"{_settings.PromptVersion}-{VotingReviews[i].Subject}", diff, parsedDiff, guidelines,
                VotingReviews[i].Categories));

        var summaries = passes
            .Select((pass, i) => (VotingReviews[i].Subject, pass.Result.Summary))
            .Where(s => !string.IsNullOrWhiteSpace(s.Summary))
            .Select(s => $"[{s.Subject}] {s.Summary}");

        var result = new ReviewResult(
            string.Join("\n", summaries),
            passes.SelectMany(p => p.Result.Findings ?? []).ToList(),
            DiscardedFindings: passes.Sum(p => p.Result.DiscardedFindings),
            OutOfScopeFindings: passes.Sum(p => p.Result.OutOfScopeFindings));

        return new ReviewPass(
            result,
            passes.Select(p => p.Model).FirstOrDefault(m => m is not null),
            passes.Sum(p => p.Cost),
            passes.Sum(p => p.InputTokens),
            passes.Sum(p => p.OutputTokens));
    }

    private static readonly (string Subject, IReadOnlyList<Category> Categories)[] VotingReviews =
    [
        ("bug", [Category.Bug]),
        ("security", [Category.Security]),
        ("performance", [Category.Performance]),
        ("convention", [Category.Convention, Category.Style, Category.Maintainability]),
    ];

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

    /// <summary>What the advisor call returned and what it cost, apart from the reviews.</summary>
    private sealed record AdvisorPass(IReadOnlyList<AdvisorHint> Hints, AdvisorTrace Trace);

    private sealed record AdvisorAnswer(List<AdvisorHint>? Hints);

    private AdvisorPass Advise(ILlmClient advisor, string diff, IReadOnlyList<Finding> findings)
    {
        var response = advisor.Request(new
        {
            max_tokens = 16000,
            reasoning = new { max_tokens = AdvisorReasoningMaxTokens },
            system = LoadPrompt(AdvisorPrompt),
            json_schema = BuildAdvisorSchema(),
            messages = new[]
            {
                new { role = "user", content = DiffBlock(diff, FindingsBlock(findings)) },
            },
        });

        var content = string.Join("\n", (response?.Content ?? [])
            .Where(b => b.Type == "text")
            .Select(b => b.Text));

        var inputTokens = response?.Usage?.InputTokens ?? 0;
        var outputTokens = response?.Usage?.OutputTokens ?? 0;

        // A reply that cannot be read is a failed call, not a call with nothing to say; it was billed all the same.
        var parsed = ParseAdvisorHints(content);
        var failed = parsed is null;

        // The schema asks for an empty snippet when a hint points at a region, so a blank one is no snippet.
        var hints = (parsed ?? [])
            .Where(h => !string.IsNullOrWhiteSpace(h.File) && !string.IsNullOrWhiteSpace(h.Check))
            .Select(h => string.IsNullOrWhiteSpace(h.Snippet) ? h with { Snippet = null } : h)
            .ToList();

        var cost = response?.Cost
            ?? CostCalculator.Estimate(_settings.AdvisorEngine, response?.Model, inputTokens, outputTokens);

        return new AdvisorPass(
            hints, new AdvisorTrace(true, response?.Model, cost, inputTokens, outputTokens, hints.Count, failed));
    }

    private static string DiffBlock(string diff, string? after) =>
        after is null ? $"```diff\n{diff}\n```" : $"```diff\n{diff}\n```\n\n{after}";

    private static string FindingsBlock(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
            return "First review findings: none.";

        var lines = findings.Select(f =>
            $"- file: {f.File}\n  snippet: {f.CodeSnippet}\n  category: {f.Category?.ToString().ToLowerInvariant()}\n  problem: {f.Problem}");
        return $"First review findings:\n{string.Join("\n", lines)}";
    }

    private static string HintsBlock(IReadOnlyList<AdvisorHint> hints)
    {
        var lines = hints.Select(h => h.Snippet is null
            ? $"- file: {h.File}\n  check: {h.Check}"
            : $"- file: {h.File}\n  snippet: {h.Snippet}\n  check: {h.Check}");
        return $"{LoadPrompt(AdvisorHintsPrompt).TrimEnd()}\n\n{string.Join("\n", lines)}";
    }

    // Rebuilt from the names the first review recorded, so a second review started from a stored
    // first review gets the same system prompt without paying for the selection again.
    private string GuidelinesOf(string? skillNames)
    {
        if (string.IsNullOrEmpty(skillNames))
            return SkillPrompt.Guidelines([], _settings.SkillPromptVersion);

        var names = skillNames.Split(',');
        var activated = _skills.Catalog()
            .Where(s => names.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(_skills.Activate)
            .ToList();
        return SkillPrompt.Guidelines(activated, _settings.SkillPromptVersion);
    }

    private static string LoadPrompt(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "prompts", $"{name}.md");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt not found: {path}");
        return File.ReadAllText(path);
    }

    private static object BuildAdvisorSchema() => new
    {
        type = "object",
        properties = new
        {
            hints = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        file = new { type = "string" },
                        snippet = new { type = "string" },
                        check = new { type = "string" },
                    },
                    required = new[] { "file", "snippet", "check" },
                    additionalProperties = false,
                },
            },
        },
        required = new[] { "hints" },
        additionalProperties = false,
    };

    /// <summary>The hints of the reply, or null when the reply is empty or is not JSON (likely cut off at the token limit).</summary>
    private static IReadOnlyList<AdvisorHint>? ParseAdvisorHints(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AdvisorAnswer>(content, JsonOptions)?.Hints ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
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
