namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The outcome of a selection: the skills to load (already filtered to names that exist in
    /// the catalog) plus what deciding cost. An empty <see cref="Names"/> is a legitimate
    /// answer — "no skill applies"; the mechanical strategies report zero cost.
    /// </summary>
    public sealed record SkillSelection(
        IReadOnlyList<string> Names, int InputTokens = 0, int OutputTokens = 0, decimal Cost = 0m)
    {
        /// <summary>
        /// True when the strategy's answer could not be read, so nothing was selected. The
        /// selection is empty either way, but "the model chose nothing" and "the model did not
        /// answer" are different facts: conflating them would hide a model that never honours the
        /// schema behind the negative cases it appears to pass.
        /// </summary>
        public bool Unreadable { get; init; }

        public static readonly SkillSelection None = new([]);
    }

    /// <summary>
    /// Decides which skills of the catalog apply to a diff (tier 1 of progressive disclosure).
    /// The review pipeline receives the decision ready-made and never learns how it was taken —
    /// model-driven, glob-driven or picked by the user are interchangeable strategies, selected
    /// by <see cref="SkillSelectorFactory"/>.
    /// </summary>
    public interface ISkillSelector
    {
        SkillSelection Select(IReadOnlyList<SkillRef> catalog, IReadOnlyList<string> files);
    }
}
