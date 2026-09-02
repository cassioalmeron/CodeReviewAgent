using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

internal class OpenAiClient(IHttpTransport transport, string model) : ILlmClient
{
    public MessageResponse Request(object requestBody)
    {
        // The incoming body uses the Anthropic shape; translate to OpenAI's
        // /v1/chat/completions request. The top-level "system" becomes a system message.
        var anthropic = JsonSerializer.SerializeToElement(requestBody);

        var messages = new List<object>();
        if (anthropic.TryGetProperty("system", out var system) &&
            system.ValueKind == JsonValueKind.String)
        {
            messages.Add(new { role = "system", content = system.GetString() });
        }
        foreach (var message in anthropic.GetProperty("messages").EnumerateArray())
        {
            messages.Add(new
            {
                role = message.GetProperty("role").GetString(),
                content = message.GetProperty("content").GetString(),
            });
        }

        var openAiRequest = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(messages),
        };

        if (anthropic.TryGetProperty("max_tokens", out var maxTokens))
            openAiRequest["max_completion_tokens"] = maxTokens.GetInt32();

        // Structured output: OpenAI wraps the schema in response_format.json_schema (strict).
        if (anthropic.TryGetProperty("json_schema", out var schema))
        {
            openAiRequest["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "response",
                    ["strict"] = true,
                    ["schema"] = JsonNode.Parse(schema.GetRawText()),
                },
            };
        }

        var body = transport.Post("/v1/chat/completions", openAiRequest.ToJsonString());

        // Map OpenAI's response shape onto the shared MessageResponse.
        var openAiResponse = JsonSerializer.Deserialize<OpenAiResponse>(body);
        var content = openAiResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        return new MessageResponse
        {
            Model = openAiResponse?.Model ?? model,
            Content = [new ContentBlock { Type = "text", Text = content }],
            Usage = new Usage
            {
                InputTokens = openAiResponse?.Usage?.PromptTokens ?? 0,
                OutputTokens = openAiResponse?.Usage?.CompletionTokens ?? 0,
            },
        };
    }
}

// Shared by OpenAiClient and OpenRouterClient — both speak the OpenAI chat-completions shape.
internal sealed class OpenAiResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("choices")]
    public List<OpenAiChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; set; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; set; }

    // OpenRouter only: the real cost (USD) of the call, returned when the request opts into
    // usage accounting (usage.include = true). Always null for OpenAI, which doesn't price responses.
    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }
}
