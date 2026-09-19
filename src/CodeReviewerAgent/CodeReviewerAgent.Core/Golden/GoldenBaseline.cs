using System.Text.Json;

namespace CodeReviewerAgent.Core.Golden;

/// <summary>
/// The configuration a golden run was measured under. Two runs are comparable only when all of it
/// matches: a different model, skills setting, prompt version, number of rounds or temperature answers a
/// different question, and comparing across them would report the difference as a regression or an
/// improvement.
/// </summary>
/// <param name="Temperature">The executor's temperature; null is the provider's default.</param>
public sealed record GoldenConfiguration(
    string Model, string Skills, string PromptVersion, int Runs, double? Temperature = null)
{
    public override string ToString() =>
        $"model {Model}, skills {Skills}, prompt {PromptVersion}, {Runs} rounds, " +
        (Temperature is { } value
            ? $"temperature {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : "default temperature");
}

/// <summary>How one case went in the baseline run.</summary>
public sealed record GoldenBaselineCase(int Successes, int CleanRounds);

/// <summary>
/// The reference a golden run is compared against in CI: how each case went, under one configuration.
/// It is versioned beside <c>cases.json</c> because the CI runner starts empty, with no database, and
/// because accepting a change on purpose should show up in the commit, as a diff of this file.
/// </summary>
public sealed record GoldenBaseline(GoldenConfiguration Configuration, IReadOnlyDictionary<string, GoldenBaselineCase> Cases)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>The baseline a scored run would leave, one entry per case, ordered by name.</summary>
    public static GoldenBaseline From(GoldenConfiguration configuration, IEnumerable<GoldenCaseResult> results) =>
        new(configuration, new SortedDictionary<string, GoldenBaselineCase>(
            results.ToDictionary(r => r.Name, r => new GoldenBaselineCase(r.Successes, r.CleanRounds)),
            StringComparer.Ordinal));

    public static GoldenBaseline Load(string path) =>
        JsonSerializer.Deserialize<GoldenBaseline>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidOperationException($"The golden baseline at {path} is empty.");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}
