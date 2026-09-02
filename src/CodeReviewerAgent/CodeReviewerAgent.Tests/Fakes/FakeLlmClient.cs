using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Llm;

namespace CodeReviewerAgent.Tests.Fakes;

/// <summary>
/// An <see cref="ILlmClient"/> that returns a pre-built response and captures the
/// request body it received, so tests can assert on the flow without a real LLM.
/// </summary>
internal sealed class FakeLlmClient : ILlmClient
{
    private readonly string _responseText;

    public FakeLlmClient(string responseText) => _responseText = responseText;

    public object? LastRequestBody { get; private set; }

    public MessageResponse Request(object requestBody)
    {
        LastRequestBody = requestBody;
        return new MessageResponse
        {
            Model = "fake-model",
            Content = [new ContentBlock { Type = "text", Text = _responseText }],
            Usage = new Usage { InputTokens = 10, OutputTokens = 20 },
        };
    }
}
