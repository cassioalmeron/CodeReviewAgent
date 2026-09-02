using System.Text.Json;
using System.Text.Json.Nodes;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

internal class OpenRouterClient(IHttpTransport transport, string model) : ILlmClient
{
    public MessageResponse Request(object requestBody)
    {
        // OpenRouter exposes an OpenAI-compatible /chat/completions endpoint; the
        // incoming body uses the Anthropic shape, so translate the same way the
        // OpenAI client does. The top-level "system" becomes a system message.
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

        var openRouterRequest = new JsonObject
        {
            ["model"] = model,
            ["messages"] = JsonSerializer.SerializeToNode(messages),
            // Opt into usage accounting so the response reports the real USD cost of the
            // call — OpenRouter fronts hundreds of models, so we read its price instead of
            // maintaining a per-model table (see MessageResponse.Cost).
            ["usage"] = new JsonObject { ["include"] = true },
        };

        if (anthropic.TryGetProperty("max_tokens", out var maxTokens))
            openRouterRequest["max_tokens"] = maxTokens.GetInt32();

        // Structured output: same response_format.json_schema shape as OpenAI.
        if (anthropic.TryGetProperty("json_schema", out var schema))
        {
            openRouterRequest["response_format"] = new JsonObject
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

        var body = transport.Post("/api/v1/chat/completions", openRouterRequest.ToJsonString());

        // OpenRouter mirrors OpenAI's response shape, so reuse the same DTOs.
        var openRouterResponse = JsonSerializer.Deserialize<OpenAiResponse>(body);
        var content = openRouterResponse?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        return new MessageResponse
        {
            Model = openRouterResponse?.Model ?? model,
            Content = [new ContentBlock { Type = "text", Text = content }],
            Usage = new Usage
            {
                InputTokens = openRouterResponse?.Usage?.PromptTokens ?? 0,
                OutputTokens = openRouterResponse?.Usage?.CompletionTokens ?? 0,
            },
            // Real cost reported by OpenRouter's usage accounting (USD); null falls back
            // to CostCalculator, which returns 0 for this engine.
            Cost = openRouterResponse?.Usage?.Cost,
        };
    }
}
