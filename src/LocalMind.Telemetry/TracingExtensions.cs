using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace LocalMind.Telemetry;

public static class TracingExtensions
{
    public static IServiceCollection AddLocalMindTracing(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        bool aspNetCore = false)
    {
        services.AddOptions<TracingOptions>()
            .Bind(configuration.GetSection(TracingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var options = configuration
            .GetSection(TracingOptions.SectionName)
            .Get<TracingOptions>() ?? new TracingOptions();

        if (!options.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: serviceName,
                serviceVersion: typeof(TracingExtensions).Assembly.GetName().Version?.ToString()))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new AlwaysOnSampler())
                    .AddSource(
                        LocalMindActivitySources.Application.Name,
                        LocalMindActivitySources.Agent.Name,
                        LocalMindActivitySources.Tools.Name,
                        LocalMindActivitySources.Cache.Name,
                        LocalMindActivitySources.Ingestion.Name)
                    .AddHttpClientInstrumentation(http => http.RecordException = true)
                    .AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(options.OtlpEndpoint);
                    });

                if (aspNetCore)
                {
                    tracing.AddAspNetCoreInstrumentation(asp =>
                    {
                        asp.RecordException = true;
                        asp.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/openapi");
                    });
                }
            });

        return services;
    }
}
