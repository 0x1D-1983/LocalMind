namespace LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class QdrantServiceExtensions
{
    public static IServiceCollection AddQdrant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IQdrantClientFactory, QdrantClientFactory>();
        services.Configure<QdrantClientOptions>(configuration.GetSection(QdrantClientOptions.SectionName));
        return services;
    }
}