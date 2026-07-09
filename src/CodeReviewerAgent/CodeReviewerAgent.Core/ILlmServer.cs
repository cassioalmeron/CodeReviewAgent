using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    public interface ILlmClient
    {
        MessageResponse Request(object requestBody);
    }

    internal class AnthropicClient(IHttpTransport transport, string model) : ILlmClient
    {
        public MessageResponse Request(object requestBody)
        {
            // Override the model from the incoming body with the configured one.
            var node = JsonSerializer.SerializeToNode(requestBody)!.AsObject();
            node["model"] = model;

            // Translate the neutral json_schema into Claude's structured-output format.
            if (node["json_schema"] is JsonNode schemaNode)
            {
                var schema = schemaNode.DeepClone();
                node.Remove("json_schema");
                node["output_config"] = new JsonObject
                {
                    ["format"] = new JsonObject
                    {
                        ["type"] = "json_schema",
                        ["schema"] = schema,
                    },
                };
            }

            var body = transport.Post("/v1/messages", node.ToJsonString());
            return JsonSerializer.Deserialize<MessageResponse>(body)
                ?? throw new InvalidOperationException("Empty response from Claude.");
        }
    }

    internal class OllamaClient(IHttpTransport transport, string model) : ILlmClient
    {
        public MessageResponse Request(object requestBody)
        {
            // The incoming body uses the Anthropic shape; reuse its messages and
            // translate to Ollama's /api/chat request. The Anthropic top-level
            // "system" field becomes a leading system message for Ollama.
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

            var ollamaRequest = new JsonObject
            {
                ["model"] = model,
                ["messages"] = JsonSerializer.SerializeToNode(messages),
                ["stream"] = false,
            };

            // Pass the JSON schema as Ollama's structured-output format.
            if (anthropic.TryGetProperty("json_schema", out var schema))
                ollamaRequest["format"] = JsonNode.Parse(schema.GetRawText());

            var body = transport.Post("/api/chat", ollamaRequest.ToJsonString());

            // Ollama uses a different response shape, so deserialize it into its own
            // DTO and map it onto the shared MessageResponse.
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(body);
            return new MessageResponse
            {
                Model = model,
                Content = [new ContentBlock { Type = "text", Text = ollamaResponse?.Message?.Content ?? string.Empty }],
                Usage = new Usage
                {
                    InputTokens = ollamaResponse?.PromptEvalCount ?? 0,
                    OutputTokens = ollamaResponse?.EvalCount ?? 0,
                },
            };
        }
    }

    internal sealed class OllamaResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }
    }

    internal sealed class OllamaMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}