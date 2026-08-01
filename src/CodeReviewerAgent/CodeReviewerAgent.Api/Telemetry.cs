using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// OpenTelemetry wiring for the viewer Api: traces + metrics over OTLP (Aspire Dashboard by
/// default). Automatic instrumentation only — incoming requests, outgoing HttpClient calls and
/// EF Core commands; the Api owns no domain metrics of its own.
/// </summary>
internal static class Telemetry
{
    private const string ServiceName = "codereviewer-api";

    public static WebApplicationBuilder AddApiTelemetry(this WebApplicationBuilder builder)
    {
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(ServiceName, serviceVersion: typeof(Telemetry).Assembly.GetName().Version?.ToString())
                .AddAttributes(
                [
                    new("deployment.environment", builder.Environment.EnvironmentName),
                    // Which store the Api is reading — 'files' produces no database spans at all.
                    new("codereviewer.storage", Environment.GetEnvironmentVariable("STORAGE") ?? "files"),
                ]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = IsInteresting)
                .AddHttpClientInstrumentation()
                // The DbContext is a singleton built outside the DI container
                // (RepositoryFactory.Create); this instrumentation is DiagnosticListener-based and
                // captures its commands regardless. The SQL text comes on db.statement by default;
                // parameter values stay off unless SetDbQueryParameters is turned on.
                .AddEntityFrameworkCoreInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        // No endpoint configured, no exporter: otherwise every export cycle retries against a
        // collector that isn't there and floods the console.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
            telemetry.UseOtlpExporter();

        return builder;
    }

    /// <summary>Swagger UI and favicon requests add spans without adding information.</summary>
    private static bool IsInteresting(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api");
}
