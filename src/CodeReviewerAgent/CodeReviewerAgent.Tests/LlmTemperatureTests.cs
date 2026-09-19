using System.Text.Json.Nodes;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The temperature reaches the provider only when it was asked for. Absent, the request is exactly
/// what it was before <c>LLM_TEMPERATURE</c> existed, so every stored run stays comparable.
/// </summary>
[Collection(EnvironmentCollection.Name)]
public class LlmTemperatureTests
{
    private static readonly object Request = new
    {
        max_tokens = 100,
        system = "review",
        messages = new[] { new { role = "user", content = "diff" } },
    };

    /// <summary>Keeps the last request body and answers with an empty completion in either shape.</summary>
    private sealed class RecordingTransport : IHttpTransport
    {
        public JsonObject? Last { get; private set; }

        public string Post(string path, string json)
        {
            Last = JsonNode.Parse(json)!.AsObject();
            return """{"model":"m","choices":[{"message":{"content":"{}"}}],"message":{"content":"{}"},"content":[]}""";
        }
    }

    [Fact]
    public void OpenAi_SendsTheTemperature_OnlyWhenSet()
    {
        var withTemperature = new RecordingTransport();
        new OpenAiClient(withTemperature, "gpt-4o-mini", 0).Request(Request);
        var without = new RecordingTransport();
        new OpenAiClient(without, "gpt-4o-mini").Request(Request);

        Assert.Equal(0, withTemperature.Last!["temperature"]!.GetValue<double>());
        Assert.False(without.Last!.ContainsKey("temperature"));
    }

    [Fact]
    public void Ollama_SendsTheTemperature_UnderOptions()
    {
        var transport = new RecordingTransport();

        new OllamaClient(transport, "qwen3:8b", 0.2).Request(Request);

        Assert.Equal(0.2, transport.Last!["options"]!["temperature"]!.GetValue<double>());
        Assert.False(transport.Last.ContainsKey("temperature"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("0", 0.0)]
    [InlineData("0.7", 0.7)]
    public void ExecutorTemperature_ReadsTheVariable(string? configured, double? expected)
    {
        var previous = Environment.GetEnvironmentVariable("LLM_TEMPERATURE");
        try
        {
            Environment.SetEnvironmentVariable("LLM_TEMPERATURE", configured);
            Assert.Equal(expected, LlmClientFactory.ExecutorTemperature());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_TEMPERATURE", previous);
        }
    }

    [Theory]
    [InlineData("hot")]
    [InlineData("-1")]
    public void ExecutorTemperature_RejectsWhatIsNotATemperature(string configured)
    {
        var previous = Environment.GetEnvironmentVariable("LLM_TEMPERATURE");
        try
        {
            Environment.SetEnvironmentVariable("LLM_TEMPERATURE", configured);
            Assert.Throws<InvalidOperationException>(() => LlmClientFactory.ExecutorTemperature());
        }
        finally
        {
            Environment.SetEnvironmentVariable("LLM_TEMPERATURE", previous);
        }
    }
}
