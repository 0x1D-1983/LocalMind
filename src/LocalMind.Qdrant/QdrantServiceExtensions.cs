namespace LocalMind.Qdrant;

using global::Qdrant.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public static class QdrantServiceExtensions
{
    public static IServiceCollection AddQdrant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QdrantClientOptions>()
            .Bind(configuration.GetSection(QdrantClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<QdrantClientOptions>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new QdrantClient(
                opts.Host,
                opts.Port,
                opts.Https,
                opts.ApiKey,
                opts.GrpcTimeout,
                loggerFactory);
        });

        return services;
    }
}