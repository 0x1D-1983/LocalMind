using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Qdrant.Client;

namespace LocalMind.Ingestion;

public static class DocumentIngestExtensions
{
    public static IServiceCollection AddDocumentIngester(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DocumentIngestOptions>()
            .Bind(configuration.GetSection(DocumentIngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<DocumentIngestOptions>>().Value;
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var qdrant = sp.GetRequiredService<QdrantClient>();
            return new DocumentIngester(ollama, qdrant, opts.CollectionName);
        });

        return services;
    }
}