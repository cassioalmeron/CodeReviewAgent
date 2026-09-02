using System.Globalization;

namespace CodeReviewerAgent.Core;

/// <summary>Shared, culture-invariant formatting helpers used across reports.</summary>
public static class Utils
{
    /// <summary>Formats a USD amount, e.g. <c>$0.012 USD</c>.</summary>
    public static string Money(decimal value) =>
        value.ToString("$0.######", CultureInfo.InvariantCulture) + " USD";

    /// <summary>Formats a score/average with one decimal place, e.g. <c>3.0</c>.</summary>
    public static string FormatScore(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}
