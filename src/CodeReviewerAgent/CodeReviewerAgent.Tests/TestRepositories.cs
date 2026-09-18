using CodeReviewerAgent.Core;
using CodeReviewerAgent.Infra;

namespace CodeReviewerAgent.Tests;

/// <summary>
/// The five EF repositories over one context, bundled as the <see cref="RepositoryContext"/>
/// the golden set takes. The repositories are thin wrappers over the shared context, so a test
/// can keep its own instances for assertions and still hand a context built here to the run.
/// </summary>
internal static class TestRepositories
{
    internal static RepositoryContext For(CodeReviewDbContext context) => new(
        new EfProjectRepository(context),
        new EfReviewRepository(context),
        new EfAssessmentRepository(context),
        new EfEvaluationRepository(context),
        new EfGoldenRunRepository(context));
}
