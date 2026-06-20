using System.Text.Json.Serialization;

namespace CodeReviewerAgent.Core
{
    /// <summary>
    /// The neutral LLM response shape shared across providers: the model id, the
    /// content blocks, and token usage.
    /// </summary>
    public sealed class MessageResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("content")]
        public List<ContentBlock> Content { get; set; } = [];

        [JsonPropertyName("usage")]
        public Usage? Usage { get; set; }
    }

    public sealed class Usage
    {
        [JsonPropertyName("input_tokens")]
        public int? InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int? OutputTokens { get; set; }
    }

    public sealed class ContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
