using System.Text.Json;
using CodeReviewerAgent.Core;
using CodeReviewerAgent.Tests.Fakes;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    /// <summary>
    /// The pairwise call (plan 015, T1): the schema puts reasoning before the verdicts, verdicts
    /// parse regardless of case, and the pairing is built from the two reviews' own prompt versions
    /// rather than anything the model wrote.
    /// </summary>
    public class JudgeTests
    {
        private static ReviewResult Review(string promptVersion, string summary) =>
            new(summary, [], "fake", "fake-model", promptVersion);

        [Fact]
        public void Evaluate_Pairwise_SchemaListsReasoningBeforeTheVerdictFields()
        {
            const string response = """
                {"reasoning":"x","correctness":"A","actionability":"A","calibration":"A",
                 "signal_to_noise":"A","conciseness":"A","overall":"A"}
                """;
            var client = new FakeLlmClient(response);
            var judge = new Judge(client, "v2");

            judge.Evaluate("diff", Review("v3", "summary a"), Review("v5", "summary b"));

            var json = JsonSerializer.Serialize(client.LastRequestBody);
            Assert.True(
                json.IndexOf("\"reasoning\"", StringComparison.Ordinal)
                    < json.IndexOf("\"correctness\"", StringComparison.Ordinal),
                "schema must list reasoning before the verdict fields, or the chain-of-thought claim is fiction");
        }

        [Fact]
        public void Evaluate_Pairwise_ParsesVerdicts_ToleratingCase()
        {
            const string response = """
                {
                    "reasoning": "B is more concrete.",
                    "correctness": "a",
                    "actionability": "B",
                    "calibration": "TIE",
                    "signal_to_noise": "A",
                    "conciseness": "tie",
                    "overall": "b"
                }
                """;
            var client = new FakeLlmClient(response);
            var judge = new Judge(client, "v2");

            var outcome = judge.Evaluate("diff", Review("v3", "a"), Review("v5", "b"));

            Assert.Equal(Verdict.A, outcome.Judgment.Correctness);
            Assert.Equal(Verdict.B, outcome.Judgment.Actionability);
            Assert.Equal(Verdict.Tie, outcome.Judgment.Calibration);
            Assert.Equal(Verdict.A, outcome.Judgment.SignalToNoise);
            Assert.Equal(Verdict.Tie, outcome.Judgment.Conciseness);
            Assert.Equal(Verdict.B, outcome.Judgment.Overall);
            Assert.Equal("B is more concrete.", outcome.Judgment.Reasoning);
        }

        [Fact]
        public void Evaluate_Pairwise_BuildsThePairingFromTheReviewsGiven_NotFromTheModel()
        {
            const string response = """
                {"reasoning":"x","correctness":"A","actionability":"A","calibration":"A",
                 "signal_to_noise":"A","conciseness":"A","overall":"A"}
                """;
            var client = new FakeLlmClient(response);
            var judge = new Judge(client, "v2");

            // "a" is v3 here; the pairing must reflect that regardless of what "A"/"B" the model wrote.
            var outcome = judge.Evaluate("diff", Review("v3", "summary a"), Review("v5", "summary b"));

            Assert.Equal("v3", outcome.Pairing.SlotA);
            Assert.Equal("v5", outcome.Pairing.SlotB);
        }
    }
}
