using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Telemetry;

public static class PrometheusMetricServerExtensions
{
    public static IServiceCollection AddPrometheusMetricServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PrometheusMetricServerOptions>()
            .Bind(configuration.GetSection(PrometheusMetricServerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<PrometheusMetricServer>();

        return services;
    }
}