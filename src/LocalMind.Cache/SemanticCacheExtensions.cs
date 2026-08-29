using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace LocalMind.Cache;

public static class SemanticCacheExtensions
{
    public static IServiceCollection AddSemanticCacheOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SemanticCacheOptions>()
            .Bind(configuration.GetSection(SemanticCacheOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EntityExtractorOptions>()
            .Bind(configuration.GetSection(EntityExtractorOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var options = sp.GetRequiredService<IOptions<EntityExtractorOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<EntityExtractor>>();
            return new EntityExtractor(ollama, options, logger);
        });

        return services;
    }
}