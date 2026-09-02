using CodeReviewerAgent.Infra;

namespace CodeReviewerAgent.Tests.Fakes;

/// <summary>
/// An <see cref="IHttpTransport"/> that plays a scripted sequence of behaviours per call —
/// each either returns a body or throws — so retry logic can be tested with no HTTP.
/// </summary>
internal sealed class FakeHttpTransport(params Func<string>[] behaviours) : IHttpTransport
{
    private readonly Queue<Func<string>> _behaviours = new(behaviours);

    public int PostCount { get; private set; }

    public string Post(string path, string json)
    {
        PostCount++;
        return _behaviours.Dequeue()();
    }
}
