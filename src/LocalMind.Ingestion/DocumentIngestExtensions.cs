using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Qdrant.Client;

namespace LocalMind.Ingestion;

public static class DocumentIngestExtensions
{
    public static IServiceCollection AddDocumentIngester(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddKnowledgeBaseOptions(configuration);
        services.AddDocumentIngestOptions(configuration);

        services.AddSingleton(sp =>
        {
            var kb = sp.GetRequiredService<IOptions<KnowledgeBaseOptions>>().Value;
            var chunk = sp.GetRequiredService<IOptions<DocumentIngestOptions>>().Value;
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var qdrant = sp.GetRequiredService<QdrantClient>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<DocumentIngester>();
            return new DocumentIngester(ollama, qdrant, kb, chunk, logger);
        });

        return services;
    }

    /// <summary>Binds index settings shared by ingest and knowledge search (chat).</summary>
    public static IServiceCollection AddKnowledgeBaseOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KnowledgeBaseOptions>()
            .Bind(configuration.GetSection(KnowledgeBaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>Binds ingest-only chunking settings.</summary>
    public static IServiceCollection AddDocumentIngestOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DocumentIngestOptions>()
            .Bind(configuration.GetSection(DocumentIngestOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
