using LocalMind.Agent.Workflow;
using LocalMind.Cache;
using LocalMind.Prompts;
using LocalMind.Telemetry;
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

        services.AddOptions<QueryRewriterOptions>()
            .Bind(configuration.GetSection(QueryRewriterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddPrompts();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();
        services.AddSingleton<LlmCallMetrics>();
        services.AddSingleton<AgentResponseProcessor>();
        services.AddSingleton<SystemPromptComposer>();
        services.AddSingleton<QueryRewriteStep>();
        services.AddSingleton<CacheStep>();
        services.AddSingleton<LlmStep>();
        services.AddSingleton<ToolStep>();
        services.AddSingleton<FinalResponseStep>();
        services.AddSingleton<AgentWorkflow>();
        services.AddSingleton<Agent>();

        services.AddSingleton(sp =>
        {
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var qdrant = sp.GetRequiredService<QdrantClient>();
            var entityExtractor = sp.GetRequiredService<EntityExtractor>();
            var options = sp.GetRequiredService<IOptions<SemanticCacheOptions>>().Value;
            return new SemanticCache<AgentResponse>(ollama, qdrant, entityExtractor, options);
        });

        services.AddSingleton(sp =>
        {
            var ollama = sp.GetRequiredService<OllamaApiClient>();
            var options = sp.GetRequiredService<IOptions<QueryRewriterOptions>>().Value;
            return new QueryRewriter(ollama, options);
        });

        services.AddHostedService(sp =>
        {
            var cache = sp.GetRequiredService<SemanticCache<AgentResponse>>();
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SemanticCacheInitializer<AgentResponse>>();
            return new SemanticCacheInitializer<AgentResponse>(cache, logger);
        });

        return services;
    }
}
