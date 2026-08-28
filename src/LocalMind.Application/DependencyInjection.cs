using LocalMind.Agent;
using LocalMind.Application.Agents;
using LocalMind.Application.Chat;
using LocalMind.Application.Conversations;
using LocalMind.Application.Knowledge;
using LocalMind.Cache;
using LocalMind.Ingestion;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using LocalMind.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddLocalMindApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOllama(configuration)
            .AddQdrant(configuration)
            .AddSemanticCacheOptions(configuration)
            .AddToolInfrastructure(configuration)
            .AddTool<KnowledgeSearchTool>()
            .AddTool<CharacterRosterTool>()
            .AddAgent(configuration)
            .AddDocumentIngester(configuration);

        services.AddSingleton<IAgentInvokeService, AgentInvokeService>();
        services.AddSingleton<IChatService, ChatService>();
        services.AddSingleton<IConversationService, ConversationService>();
        services.AddSingleton<IKnowledgeDocumentService, KnowledgeDocumentService>();

        return services;
    }
}
