using CodeReviewerAgent.Core;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

ILlmClient client = LlmClientFactory.Create();
IDiffSource diffSource = DiffSourceFactory.Create(args);

var reviewer = new CodeReviewer(client, diffSource.GetDiff());
reviewer.Review();
