namespace CodeReviewerAgent.Tests.Fakes;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that returns a scripted sequence of responses,
/// so HTTP behaviour (success, 5xx, 429, network error, timeout) can be simulated with
/// no real network. A factory that throws simulates a transport failure.
/// </summary>
internal sealed class StubHttpMessageHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);

    public int SendCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        SendCount++;
        try
        {
            return Task.FromResult(_responses.Dequeue()());
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}
