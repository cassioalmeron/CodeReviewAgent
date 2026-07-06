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

        /// <summary>
        /// Real cost (USD) reported by the provider, when available (Claude Code).
        /// Null for engines that don't report it — the cost is then estimated from tokens.
        /// Filled by the client adapter, never deserialized from the API payload.
        /// </summary>
        [JsonIgnore]
        public decimal? Cost { get; set; }
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
