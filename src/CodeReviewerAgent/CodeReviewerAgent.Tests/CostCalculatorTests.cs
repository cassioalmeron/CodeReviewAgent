using CodeReviewerAgent.Core;
using Xunit;

namespace CodeReviewerAgent.Tests
{
    public class CostCalculatorTests
    {
        [Theory]
        [InlineData("ollama")]
        [InlineData("Ollama")]
        [InlineData("")]
        [InlineData(null)]
        public void Estimate_NonClaudeEngine_IsFree(string? engine)
        {
            Assert.Equal(0m, CostCalculator.Estimate(engine, "claude-opus-4-8", 1_000_000, 1_000_000));
        }

        [Fact]
        public void Estimate_ClaudeEngineWithNullModel_IsFree()
        {
            Assert.Equal(0m, CostCalculator.Estimate("claude", null, 1_000_000, 1_000_000));
        }

        [Fact]
        public void Estimate_UnknownModel_IsFree()
        {
            Assert.Equal(0m, CostCalculator.Estimate("claude", "gpt-4o", 1_000_000, 1_000_000));
        }

        [Fact]
        public void Estimate_KnownModel_PricesInputAndOutputTokens()
        {
            // opus pricing: $5 / 1M input, $25 / 1M output.
            var cost = CostCalculator.Estimate("claude", "claude-opus-4-8", 2_000_000, 1_000_000);
            Assert.Equal(35m, cost);
        }

        [Fact]
        public void Estimate_EngineMatchIsCaseInsensitive()
        {
            var cost = CostCalculator.Estimate("Claude", "claude-opus-4-8", 1_000_000, 0);
            Assert.Equal(5m, cost);
        }

        [Fact]
        public void Estimate_ModelMatchIsCaseInsensitive()
        {
            var cost = CostCalculator.Estimate("claude", "CLAUDE-OPUS-4-8", 1_000_000, 0);
            Assert.Equal(5m, cost);
        }

        [Fact]
        public void Estimate_ModelMatchedAsSubstring()
        {
            // Full API model ids often carry a date suffix.
            var cost = CostCalculator.Estimate("claude", "claude-haiku-4-5-20251001", 1_000_000, 1_000_000);
            Assert.Equal(6m, cost);
        }

        [Theory]
        [InlineData("claude-fable-5", 10, 50)]
        [InlineData("claude-opus-4-8", 5, 25)]
        [InlineData("claude-opus-4-7", 5, 25)]
        [InlineData("claude-opus-4-6", 5, 25)]
        [InlineData("claude-opus-4-5", 5, 25)]
        [InlineData("claude-sonnet-4-6", 3, 15)]
        [InlineData("claude-sonnet-4-5", 3, 15)]
        [InlineData("claude-haiku-4-5", 1, 5)]
        public void Estimate_EachKnownModel_UsesItsPricing(string model, int inputPrice, int outputPrice)
        {
            var cost = CostCalculator.Estimate("claude", model, 1_000_000, 1_000_000);
            Assert.Equal(inputPrice + outputPrice, cost);
        }

        [Fact]
        public void Estimate_ZeroTokens_IsFree()
        {
            Assert.Equal(0m, CostCalculator.Estimate("claude", "claude-opus-4-8", 0, 0));
        }

        [Fact]
        public void Estimate_PartialMillionTokens_ProratesCost()
        {
            // 500k input @ $5/1M = $2.5; 100k output @ $25/1M = $2.5.
            var cost = CostCalculator.Estimate("claude", "claude-opus-4-8", 500_000, 100_000);
            Assert.Equal(5m, cost);
        }
    }
}
