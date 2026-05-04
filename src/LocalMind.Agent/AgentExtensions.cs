using LocalMind.Cache;
using LocalMind.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Qdrant.Client;

namespace LocalMind.Agent;

public static class AgentExtensions
{
    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();

        services.AddSingleton(sp =>
        {
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var qdrant = sp.GetRequiredService<QdrantClient>();
            var options = sp.GetRequiredService<IOptions<SemanticCacheOptions>>().Value;
            return new SemanticCache<AgentResponse>(ollama, qdrant, options);
        });

        services.AddHostedService(sp =>
        {
            var cache = sp.GetRequiredService<SemanticCache<AgentResponse>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SemanticCacheInitializer<AgentResponse>>();
            return new SemanticCacheInitializer<AgentResponse>(cache, logger);
        });

        services.AddSingleton(sp =>
        {
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var executor = sp.GetRequiredService<ToolExecutor>();
            var manifest = sp.GetRequiredService<ToolManifestBuilder>();
            var conversationStore = sp.GetRequiredService<IConversationStore>();
            var agentOptions = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
            var semanticCacheOptions = sp.GetRequiredService<IOptions<SemanticCacheOptions>>().Value;
            var semanticCache = sp.GetRequiredService<SemanticCache<AgentResponse>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<Agent>();
            var structuredOutputParser = sp.GetRequiredService<IStructuredOutputParser>();
            
            return new Agent(ollama, executor, manifest, conversationStore, semanticCache, agentOptions, semanticCacheOptions, logger, structuredOutputParser);
        });

        return services;
    }
}