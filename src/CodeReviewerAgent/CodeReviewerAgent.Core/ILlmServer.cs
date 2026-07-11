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
    }
}