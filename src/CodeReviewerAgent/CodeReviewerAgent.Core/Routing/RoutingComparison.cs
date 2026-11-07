using CodeReviewerAgent.Core.Golden;

namespace CodeReviewerAgent.Core.Routing;

/// <summary>
/// One recorded pass over every case, by one model. Cases from several eval sets go into the same
/// pass, because the comparison does not care which set a case came from. The rounds carry what
/// the case results do not: the diff, which the route is decided on, and the cost of each round.
/// </summary>
public sealed record RoutingPass(
    string Model, IReadOnlyList<GoldenCaseResult> Results, IReadOnlyList<CompletedRound> Rounds);

/// <summary>One case seen from both passes, with the route the rule gave it.</summary>
public sealed record RoutedCase(
    string Name,
    GoldenKind Kind,
    ModelRoute Route,
    DiffSize Size,
    GoldenCaseResult Cheap,
    GoldenCaseResult Strong,
    decimal CheapCost,
    decimal StrongCost)
{
    public GoldenCaseResult Selected => Route == ModelRoute.Cheap ? Cheap : Strong;

    public decimal SelectedCost => Route == ModelRoute.Cheap ? CheapCost : StrongCost;
}

/// <summary>One column of the comparison: the five gates and the total cost.</summary>
public sealed record RoutingColumn(
    string Label, IReadOnlyList<GoldenCaseResult> Results, ModelVerdict Verdict, decimal Cost);

/// <summary>
/// For a cheap-route case, whether the strong pass did better than the cheap one. Routing can only
/// lose here: on a strong-route case the rule already chose the strong model.
/// </summary>
/// <param name="StrongFoundMore">
/// Detection cases only: the strong pass found the planted defect in more rounds than the cheap
/// one. A trap has nothing to find, so it is always false there and the rates speak for themselves.
/// </param>
public sealed record CheapRouteQuality(
    string Name, GoldenKind Kind, int Runs, int CheapSuccesses, int StrongSuccesses, bool StrongFoundMore);

public sealed record RoutingComparison(
    string CheapModel,
    string StrongModel,
    IReadOnlyList<RoutedCase> Cases,
    RoutingColumn Cheap,
    RoutingColumn Routed,
    RoutingColumn Strong,
    IReadOnlyList<CheapRouteQuality> CheapRouteQuality)
{
    public int CheapRouteCases => Cases.Count(c => c.Route == ModelRoute.Cheap);

    public int StrongRouteCases => Cases.Count(c => c.Route == ModelRoute.Strong);

    /// <summary>
    /// Composes the three columns from two recorded passes, with no model call: the routed column
    /// takes, per case, the result of the pass the case's route selects.
    /// </summary>
    public static RoutingComparison Compose(RoutingPass cheap, RoutingPass strong)
    {
        var cheapByName = ByName(cheap);
        var strongByName = ByName(strong);

        var missing = cheapByName.Keys.Except(strongByName.Keys)
            .Concat(strongByName.Keys.Except(cheapByName.Keys))
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"The two passes do not cover the same cases; present in only one: {string.Join(", ", missing)}.");

        var cases = new List<RoutedCase>();
        foreach (var (name, cheapResult) in cheapByName)
        {
            var strongResult = strongByName[name];
            if (cheapResult.Runs != strongResult.Runs)
                throw new InvalidOperationException(
                    $"Case '{name}' ran {cheapResult.Runs} round(s) in the cheap pass and {strongResult.Runs} in the strong one; the passes must use the same number of rounds.");

            var diff = DiffOf(name, cheap, strong);
            cases.Add(new RoutedCase(
                name,
                cheapResult.Kind,
                RoutingRule.Route(diff),
                RoutingRule.Measure(diff),
                cheapResult,
                strongResult,
                CostOf(name, cheap),
                CostOf(name, strong)));
        }

        var quality = cases
            .Where(c => c.Route == ModelRoute.Cheap)
            .Select(c => new CheapRouteQuality(
                c.Name, c.Kind, c.Cheap.Runs, c.Cheap.Successes, c.Strong.Successes,
                c.Kind == GoldenKind.Detection && c.Strong.Successes > c.Cheap.Successes))
            .ToList();

        return new RoutingComparison(
            cheap.Model,
            strong.Model,
            cases,
            Column("Cheap everywhere", cases, c => c.Cheap, c => c.CheapCost),
            Column("Routed", cases, c => c.Selected, c => c.SelectedCost),
            Column("Strong everywhere", cases, c => c.Strong, c => c.StrongCost),
            quality);
    }

    private static RoutingColumn Column(
        string label,
        IReadOnlyList<RoutedCase> cases,
        Func<RoutedCase, GoldenCaseResult> result,
        Func<RoutedCase, decimal> cost)
    {
        var results = cases.Select(result).ToList();
        return new RoutingColumn(label, results, GoldenGates.Judge(results), cases.Sum(cost));
    }

    private static Dictionary<string, GoldenCaseResult> ByName(RoutingPass pass)
    {
        var duplicate = pass.Results.GroupBy(r => r.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"The {pass.Model} pass holds case '{duplicate.Key}' more than once; case names must be unique across the sets.");

        return pass.Results.ToDictionary(r => r.Name, StringComparer.Ordinal);
    }

    private static decimal CostOf(string name, RoutingPass pass) =>
        pass.Rounds.Where(r => r.Case == name).Sum(r => r.Review.Cost);

    private static string DiffOf(string name, RoutingPass cheap, RoutingPass strong) =>
        cheap.Rounds.Concat(strong.Rounds).FirstOrDefault(r => r.Case == name)?.Diff
        ?? throw new InvalidOperationException(
            $"Case '{name}' has no recorded round in either pass, so its diff and its route cannot be known.");
}
