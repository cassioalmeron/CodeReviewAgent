namespace CodeReviewerAgent.Infra.Configurations;

/// <summary>
/// Every monetary column has the same shape. The cheapest paid review of the US-013 matrix cost
/// US$ 0.00006774, so eight decimal places; fewer and the per-run sums stop adding up to the totals
/// that were published. Extra places the providers return are floating-point noise, not precision.
/// </summary>
internal static class CostPrecision
{
    public const int Precision = 18;
    public const int Scale = 8;
}
