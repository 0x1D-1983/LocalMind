using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Cache;

public static class SemanticCacheExtensions
{
    public static IServiceCollection AddSemanticCacheOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SemanticCacheOptions>()
            .Bind(configuration.GetSection(SemanticCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}