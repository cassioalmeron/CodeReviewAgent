using CodeReviewerAgent.Core;
using CodeReviewerAgent.Core.Advisor;
using Xunit;

namespace CodeReviewerAgent.Tests.Core.Advisor;

public class AdvisorTriggerTests
{
    [Fact]
    public void AdvisorTriggerTests_No_findings_escalates() =>
        Assert.True(AdvisorTrigger.ShouldEscalate([]));

    [Fact]
    public void AdvisorTriggerTests_Convention_findings_only_escalate()
    {
        Finding[] findings = [FindingOf(Category.Convention), FindingOf(Category.Convention)];

        Assert.True(AdvisorTrigger.ShouldEscalate(findings));
    }

    [Fact]
    public void AdvisorTriggerTests_A_Performance_finding_only_escalates()
    {
        Finding[] findings = [FindingOf(Category.Performance)];

        Assert.True(AdvisorTrigger.ShouldEscalate(findings));
    }

    [Fact]
    public void AdvisorTriggerTests_One_Bug_among_Conventions_does_not_escalate()
    {
        Finding[] findings = [FindingOf(Category.Convention), FindingOf(Category.Bug), FindingOf(Category.Convention)];

        Assert.False(AdvisorTrigger.ShouldEscalate(findings));
    }

    [Fact]
    public void AdvisorTriggerTests_One_Security_finding_does_not_escalate()
    {
        Finding[] findings = [FindingOf(Category.Security)];

        Assert.False(AdvisorTrigger.ShouldEscalate(findings));
    }

    /// <summary>
    /// The version names this rule: Bug and Security are the categories that keep the alarm quiet.
    /// Changing the rule without bumping the version fails here, so a rule version cannot drift in
    /// silence from the behavior it stands for.
    /// </summary>
    [Fact]
    public void AdvisorTriggerTests_Version_v1_means_a_Bug_or_a_Security_finding_keeps_the_alarm_quiet()
    {
        Assert.Equal("v1", AdvisorTrigger.Version);
        Assert.False(AdvisorTrigger.ShouldEscalate([FindingOf(Category.Bug)]));
        Assert.False(AdvisorTrigger.ShouldEscalate([FindingOf(Category.Security)]));
        Assert.All(
            Enum.GetValues<Category>().Where(c => c is not (Category.Bug or Category.Security)),
            c => Assert.True(AdvisorTrigger.ShouldEscalate([FindingOf(c)]), $"{c} should not keep the alarm quiet."));
    }

    private static Finding FindingOf(Category category) =>
        new("src/A.cs", "var x = 1;", Severity.Warning, category, "A problem.", "A suggestion.");
}
