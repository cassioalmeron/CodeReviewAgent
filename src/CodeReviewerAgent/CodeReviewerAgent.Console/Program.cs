using CodeReviewerAgent.Core;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

ILlmClient client = LlmClientFactory.Create();

// `pr <number>` reviews a real pull request; no args reviews the local HEAD diff.
IDiffSource diffSource = args is ["pr", var prArg, ..] && int.TryParse(prArg, out var prNumber)
    ? new PullRequestDiffSource(prNumber)
    : new LocalDiffSource();

var reviewer = new CodeReviewer(client, diffSource);
reviewer.Review();
