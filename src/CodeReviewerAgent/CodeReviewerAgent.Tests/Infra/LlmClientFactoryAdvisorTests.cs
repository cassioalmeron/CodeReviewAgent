using CodeReviewerAgent.Infra;
using Xunit;

namespace CodeReviewerAgent.Tests.Infra;

[Collection(EnvironmentCollection.Name)]
public class LlmClientFactoryAdvisorTests
{
    [Theory]
    [InlineData("single")]
    [InlineData("voting")]
    public void LlmClientFactoryAdvisorTests_Under_other_patterns_no_advisor_is_built_and_ADVISOR_ENGINE_is_not_read(string pattern)
    {
        // An engine name that would throw if it were read proves the variable is not touched.
        WithEnvironment([("ADVISOR_ENGINE", "not-an-engine")], () =>
            Assert.Null(LlmClientFactory.CreateAdvisorFor(pattern)));
    }

    [Fact]
    public void LlmClientFactoryAdvisorTests_The_advisor_pattern_without_ADVISOR_ENGINE_fails_naming_the_variable()
    {
        WithEnvironment([("ADVISOR_ENGINE", null)], () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LlmClientFactory.CreateAdvisorFor("advisor"));

            Assert.Contains("ADVISOR_ENGINE is not configured", exception.Message);
        });
    }

    [Fact]
    public void LlmClientFactoryAdvisorTests_An_unknown_advisor_engine_names_ADVISOR_ENGINE_in_the_message()
    {
        WithEnvironment([("ADVISOR_ENGINE", "not-an-engine")], () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LlmClientFactory.CreateAdvisor());

            Assert.Contains("Unknown ADVISOR_ENGINE 'not-an-engine'", exception.Message);
        });
    }

    [Fact]
    public void LlmClientFactoryAdvisorTests_The_advisor_reads_the_model_variable_of_its_own_engine()
    {
        WithEnvironment([("ADVISOR_ENGINE", "ollama"), ("OLLAMA_MODEL", null)], () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LlmClientFactory.CreateAdvisor());

            Assert.Contains("OLLAMA_MODEL", exception.Message);
        });

        WithEnvironment([("ADVISOR_ENGINE", "ollama"), ("OLLAMA_MODEL", "some-model"), ("OLLAMA_HOST", "http://localhost:11434")], () =>
            Assert.NotNull(LlmClientFactory.CreateAdvisorFor("advisor")));
    }

    [Fact]
    public void LlmClientFactoryAdvisorTests_The_executor_still_reads_LLM_ENGINE_and_names_it_when_missing()
    {
        WithEnvironment([("LLM_ENGINE", null)], () =>
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LlmClientFactory.CreateWithModel());

            Assert.Contains("LLM_ENGINE is not configured", exception.Message);
        });
    }

    private static void WithEnvironment((string Name, string? Value)[] variables, Action action)
    {
        var previous = variables.Select(v => (v.Name, Value: Environment.GetEnvironmentVariable(v.Name))).ToList();
        try
        {
            foreach (var (name, value) in variables)
                Environment.SetEnvironmentVariable(name, value);
            action();
        }
        finally
        {
            foreach (var (name, value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
