using CodeReviewerAgent.Console;
using System.Text.Json.Serialization;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

ILlmClient client = LlmClientFactory.Create();

// `pr <number>` reviews a real pull request; no args reviews the local HEAD diff.
IDiffSource diffSource = args is ["pr", var prArg, ..] && int.TryParse(prArg, out var prNumber)
    ? new PullRequestDiffSource(prNumber)
    : new LocalDiffSource();

var reviewer = new CodeReviewer(client, diffSource);
reviewer.Review();

sealed class MessageResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = [];

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

sealed class Usage
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; set; }
}

sealed class ContentBlock
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}
