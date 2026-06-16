using CodeReviewerAgent.Console;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

EnvLoader.Load(Path.Combine(AppContext.BaseDirectory, ".env"));

var requestBody = new
{
    max_tokens = 16000,
    // Not supported on Haiku 4.5 — enable only for Opus/Sonnet 4.6+ models.
    //thinking = new { type = "adaptive" },
    //output_config = new { effort = "high" },
    messages = new[]
    {
        new { role = "user", content = "What is the capital of France?" },
    },
};

var json = JsonSerializer.Serialize(requestBody);
using var content = new StringContent(json, Encoding.UTF8, "application/json");

ILlmClient client = LlmClientFactory.Create();

var stopwatch = Stopwatch.StartNew();
var message = client.Request(requestBody);
stopwatch.Stop();

foreach (var block in message?.Content ?? [])
    if (block.Type == "text")
        Console.WriteLine(block.Text);

if (message?.Usage is { } usage)
{
    var inputTokens = usage.InputTokens ?? 0;
    var outputTokens = usage.OutputTokens ?? 0;
    Console.WriteLine($"[tokens] input: {inputTokens}, output: {outputTokens}, total: {inputTokens + outputTokens}");
}

Console.WriteLine($"[latency] {stopwatch.ElapsedMilliseconds} ms");

sealed class MessageResponse
{
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