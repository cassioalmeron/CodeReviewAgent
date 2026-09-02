using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Skill;

namespace CodeReviewerAgent.Tests.Fakes;

/// <summary>
/// An <see cref="ISkillSelector"/> that returns a fixed choice and records what it was asked,
/// so review tests don't depend on a real selection call.
/// </summary>
internal sealed class FakeSkillSelector(params string[] names) : ISkillSelector
{
    public int Calls { get; private set; }

    public IReadOnlyList<string>? LastFiles { get; private set; }

    public SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files)
    {
        Calls++;
        LastFiles = files;
        return new SkillSelection(names, 7, 3, 0.5m);
    }
}
