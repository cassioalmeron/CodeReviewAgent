using CodeReviewerAgent.Core;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

// `judge` scores the reviews persisted by `eval`; it builds its own (stronger) client.
if (args is ["judge", ..])
{
    JudgeRunner.Run();
    return;
}

ILlmClient client = LlmClientFactory.Create();

// `eval` runs the golden set (detection only).
if (args is ["eval", ..])
{
    RunGoldenSet(client);
    return;
}

// `all` runs the full evaluation cycle in one go: golden set, then the judge over its results.
if (args is ["all", ..])
{
    RunGoldenSet(client);
    JudgeRunner.Run();
    return;
}

IDiffSource diffSource = DiffSourceFactory.Create(args);
try
{
    var review = CodeReviewer.ReviewAndReport(client, diffSource.GetDiff());

    // Publish the review to the PR when asked: `pr <n> --publish`.
    if (args is ["pr", var prArg, ..] && args.Contains("--publish") && int.TryParse(prArg, out var prNumber))
        PrPublisher.Publish(prNumber, review);
}
catch (Exception ex) when (args is ["pr", ..])
{
    // Graceful skip: the agent must never block the PR. Log to stderr and exit cleanly.
    Console.Error.WriteLine($"Review skipped due to an error: {ex.Message}");
}

static void RunGoldenSet(ILlmClient executor)
{
    var results = GoldenEvaluator.Run(executor);
    Console.WriteLine();
    Console.WriteLine("=== Golden set ===");
    foreach (var r in results)
        Console.WriteLine(GoldenEvaluator.FormatLine(r));
    Console.WriteLine($"Golden set: {results.Sum(r => r.Detections)}/{results.Sum(r => r.Runs)} detections");
}
