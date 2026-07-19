using System.Text.Json;
using System.Text.Json.Nodes;
using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Infra
{
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
}
