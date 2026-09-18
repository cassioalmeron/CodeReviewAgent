namespace CodeReviewerAgent.Core.Skill;

/// <summary>
/// Where the skills come from: the catalog offered to the selector, and the instructions of the
/// ones it chose. The review and the trigger eval depend on this rather than on the disk, so a test
/// can hand them a catalog of its own.
/// <para>
/// It matters because <c>assets/skills/</c> is deliberately not versioned: a fresh clone, the CI
/// runner among them, has no skills at all, and tests that read that folder pass on one machine and
/// fail on the next.
/// </para>
/// </summary>
public interface ISkillSource
{
    IReadOnlyList<SkillRef> Catalog();

    ActivatedSkill Activate(SkillRef skill);
}

/// <summary>The skills on disk, under <c>assets/skills/</c> unless another root is given.</summary>
public sealed class FileSkillSource(string? root = null) : ISkillSource
{
    public IReadOnlyList<SkillRef> Catalog() => SkillCatalog.Discover(root).Skills;

    public ActivatedSkill Activate(SkillRef skill) => SkillCatalog.Activate(skill);
}
