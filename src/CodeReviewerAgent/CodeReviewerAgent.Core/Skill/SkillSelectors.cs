namespace CodeReviewerAgent.Core.Skill;

/// <summary>
/// Mechanical selection by the <c>metadata.applies-to</c> globs — the behaviour the agent had
/// before model-driven activation. Kept as a first-class strategy, chosen explicitly: it
/// is the control group when measuring the selection call against the golden set.
/// </summary>
public sealed class GlobSkillSelector : ISkillSelector
{
    public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
        new(SkillCatalog.MatchByGlobs(catalog, files).Select(s => s.Name).ToList());
}

/// <summary>
/// User-explicit activation (<c>SKILLS=csharp,react</c>): the named skills are loaded without
/// asking the model. Names absent from the catalog are ignored.
/// </summary>
public sealed class ExplicitSkillSelector(IEnumerable<string> names) : ISkillSelector
{
    private readonly string[] _names = [.. names];

    public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
        new(catalog.Where(s => _names.Contains(s.Name, StringComparer.OrdinalIgnoreCase))
            .Select(s => s.Name).ToList());
}

/// <summary>Skills disabled (<c>SKILLS=off</c>): nothing is loaded and nothing is asked.</summary>
public sealed class NoSkillSelector : ISkillSelector
{
    public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files) =>
        SkillSelection.None;
}
