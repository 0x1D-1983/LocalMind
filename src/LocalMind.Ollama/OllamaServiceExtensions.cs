using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace LocalMind.Ollama;

public static class OllamaServiceExtensions
{
    public static IServiceCollection AddOllama(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OllamaApiClientOptions>()
            .Bind(configuration.GetSection(OllamaApiClientOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient("ollama", (sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaApiClientOptions>>().Value;
            client.BaseAddress = new Uri(opts.BaseUrl);
        });

        services.AddSingleton(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama");
            return new OllamaApiClient(httpClient);
        });

        return services;
    }
}