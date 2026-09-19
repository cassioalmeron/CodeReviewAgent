using System.Text.Json;
using System.Text.Json.Nodes;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Infra;

/// <param name="temperature">Sent only when set; null leaves the provider's default, which is what
/// every run did before <c>LLM_TEMPERATURE</c> existed.</param>
internal class AnthropicClient(IHttpTransport transport, string model, double? temperature = null) : ILlmClient
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

        if (temperature is { } value)
            node["temperature"] = value;

        var body = transport.Post("/v1/messages", node.ToJsonString());
        return JsonSerializer.Deserialize<MessageResponse>(body)
            ?? throw new InvalidOperationException("Empty response from Claude.");
    }
}
