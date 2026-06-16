using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Console
{
    internal interface ILlmClient
    {
        MessageResponse Request(object requestBody);
    }

    internal class AnthropicClient : ILlmClient
    {
        private readonly HttpClient _http;
        private readonly string _model;
        public AnthropicClient()
        {
            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not configured. Add it to the .env file.");

            _model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")
                ?? throw new InvalidOperationException("ANTHROPIC_MODEL is not configured. Add it to the .env file.");

            _http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
            _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }
        public MessageResponse Request(object requestBody)
        {
            // Override the model from the incoming body with the configured one.
            var node = JsonSerializer.SerializeToNode(requestBody)!.AsObject();
            node["model"] = _model;

            var json = node.ToJsonString();
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = _http.PostAsync("/v1/messages", content).GetAwaiter().GetResult();
            var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Claude request failed ({(int)response.StatusCode}): {responseJson}");
            return JsonSerializer.Deserialize<MessageResponse>(responseJson);
        }
    }

    internal class OllamaClient : ILlmClient
    {
        private readonly HttpClient _http;
        private readonly string _model;

        public OllamaClient()
        {
            _model = Environment.GetEnvironmentVariable("OLLAMA_MODEL")
                ?? throw new InvalidOperationException("OLLAMA_MODEL is not configured. Add it to the .env file.");

            var host = Environment.GetEnvironmentVariable("OLLAMA_HOST") ?? "http://localhost:11434";
            _http = new HttpClient { BaseAddress = new Uri(host) };
        }

        public MessageResponse Request(object requestBody)
        {
            // The incoming body uses the Anthropic shape; reuse its messages and
            // translate to Ollama's /api/chat request.
            var anthropic = JsonSerializer.SerializeToElement(requestBody);
            var ollamaRequest = new
            {
                model = _model,
                messages = anthropic.GetProperty("messages"),
                stream = false,
            };

            var json = JsonSerializer.Serialize(ollamaRequest);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = _http.PostAsync("/api/chat", content).GetAwaiter().GetResult();
            var responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Ollama request failed ({(int)response.StatusCode}): {responseJson}");

            // Ollama uses a different response shape, so deserialize it into its own
            // DTO and map it onto the shared MessageResponse.
            var ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseJson);
            return new MessageResponse
            {
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