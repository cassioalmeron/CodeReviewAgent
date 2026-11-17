using System.Text.Json.Nodes;
using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests.Infra;

public class OpenRouterClientTests
{
    private const string Completion = """{"model":"m","choices":[{"message":{"content":"{}"}}]}""";

    private sealed class RecordingTransport : IHttpTransport
    {
        public string? LastJson { get; private set; }

        public string Post(string path, string json)
        {
            LastJson = json;
            return Completion;
        }
    }

    [Fact]
    public void OpenRouterClientTests_A_reasoning_cap_in_the_body_is_forwarded_as_is_under_reasoning()
    {
        var transport = new RecordingTransport();
        var request = new
        {
            max_tokens = 16000,
            reasoning = new { max_tokens = 12000 },
            system = "advise",
            messages = new[] { new { role = "user", content = "diff" } },
        };

        new OpenRouterClient(transport, "deepseek/model").Request(request);

        var sent = JsonNode.Parse(transport.LastJson!)!.AsObject();
        Assert.Equal(16000, sent["max_tokens"]!.GetValue<int>());
        Assert.Equal("""{"max_tokens":12000}""", sent["reasoning"]!.ToJsonString());
    }

    [Fact]
    public void OpenRouterClientTests_A_body_without_reasoning_produces_the_request_it_always_did()
    {
        var transport = new RecordingTransport();
        var request = new
        {
            max_tokens = 100,
            system = "review",
            messages = new[] { new { role = "user", content = "diff" } },
        };

        new OpenRouterClient(transport, "deepseek/model").Request(request);

        Assert.Equal(
            """{"model":"deepseek/model","messages":[{"role":"system","content":"review"},{"role":"user","content":"diff"}],"usage":{"include":true},"max_tokens":100}""",
            transport.LastJson);
    }
}
