namespace CodeReviewerAgent.Core.Judge;

/// <summary>How the absolute judge produces its scores: one call for all criteria, or one call per criterion.</summary>
public enum JudgePattern { Single, Sectioned }
