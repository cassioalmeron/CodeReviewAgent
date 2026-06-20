using CodeReviewerAgent.Core;

namespace CodeReviewerAgent.Tests.Fakes
{
    /// <summary>
    /// An <see cref="IDiffSource"/> that returns a canned diff and records whether
    /// it was asked for one.
    /// </summary>
    internal sealed class FakeDiffSource : IDiffSource
    {
        private readonly string _diff;

        public FakeDiffSource(string diff) => _diff = diff;

        public bool WasCalled { get; private set; }

        public string GetDiff()
        {
            WasCalled = true;
            return _diff;
        }
    }
}
