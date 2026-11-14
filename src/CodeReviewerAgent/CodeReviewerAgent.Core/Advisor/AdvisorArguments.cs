using System.Globalization;

namespace CodeReviewerAgent.Core.Advisor;

/// <summary>The flags of one advisor command, read once: values by name and the switches that were present.</summary>
internal sealed record AdvisorArguments(
    IReadOnlyDictionary<string, List<string>> Values, IReadOnlySet<string> Switches)
{
    public string? One(string name) => Values.TryGetValue(name, out var values) ? values[0] : null;

    public IReadOnlyList<string> Many(string name) => Values.TryGetValue(name, out var values) ? values : [];

    /// <summary>
    /// Reads <paramref name="args"/> against the flags a command knows. A flag takes the value after
    /// it, a switch takes none, and only the names in <paramref name="repeatable"/> may appear twice.
    /// Returns null with <paramref name="error"/> set when the arguments do not fit.
    /// </summary>
    public static AdvisorArguments? Read(
        IReadOnlyList<string> args, IReadOnlyCollection<string> valueFlags, IReadOnlyCollection<string> repeatable,
        IReadOnlyCollection<string> switches, out string? error)
    {
        error = null;
        var values = new Dictionary<string, List<string>>();
        var present = new HashSet<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (switches.Contains(name))
            {
                present.Add(name);
                continue;
            }

            if (!valueFlags.Contains(name))
            {
                error = $"Unknown argument '{name}'.";
                return null;
            }

            if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--"))
            {
                error = $"{name} needs a value.";
                return null;
            }

            if (values.TryGetValue(name, out var existing) && !repeatable.Contains(name))
            {
                error = $"{name} was given more than once.";
                return null;
            }

            if (existing is null)
                values[name] = existing = [];
            existing.Add(args[++i].Trim());
        }

        return new AdvisorArguments(values, present);
    }

    public static string Money(decimal cost) => $"US$ {cost.ToString("0.0000", CultureInfo.InvariantCulture)}";
}
