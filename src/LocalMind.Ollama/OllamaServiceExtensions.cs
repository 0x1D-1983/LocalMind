using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Ollama;

public static class OllamaServiceExtensions
{
    public static IServiceCollection AddOllama(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOllamaApiClientFactory, OllamaApiClientFactory>();
        services.Configure<OllamaApiClientOptions>(configuration.GetSection(OllamaApiClientOptions.SectionName));
        return services;
    }
}