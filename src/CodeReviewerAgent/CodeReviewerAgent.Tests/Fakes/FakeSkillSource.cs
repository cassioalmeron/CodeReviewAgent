using CodeReviewerAgent.Core.Skill;

namespace CodeReviewerAgent.Tests.Fakes;

/// <summary>
/// An <see cref="ISkillSource"/> held in memory, so the review and the trigger eval can be tested
/// without the skills on disk. <c>assets/skills/</c> is not versioned, so a test that read it would
/// pass on a machine that has the folder and fail on a fresh clone, the CI runner among them.
/// </summary>
internal sealed class FakeSkillSource(params FakeSkill[] skills) : ISkillSource
{
    /// <summary>The two skills the tests rely on, shaped like the bundled ones.</summary>
    public static FakeSkillSource Default { get; } = new(
        new FakeSkill("csharp", "C# conventions for .cs files", "*.cs",
            "Use PascalCase for public members and file-scoped namespaces."),
        new FakeSkill("react", "React conventions for .tsx and .ts files", "*.tsx,*.ts",
            "Style components with styled-components, never inline styles."));

    public IReadOnlyList<SkillRef> Catalog() =>
        [.. skills.Select(s => new SkillRef(s.Name, s.Description, $"memory/{s.Name}/SKILL.md", $"memory/{s.Name}")
        {
            Metadata = new Dictionary<string, string> { ["applies-to"] = s.AppliesTo },
        })];

    public ActivatedSkill Activate(SkillRef skill) =>
        new(skill.Name, skill.Directory, skills.Single(s => s.Name == skill.Name).Body, []);
}

internal sealed record FakeSkill(string Name, string Description, string AppliesTo, string Body);
