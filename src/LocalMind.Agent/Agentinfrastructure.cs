using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using LocalMind.Ollama;

namespace LocalMind.Agent;

// ─────────────────────────────────────────────────────────────────────────────
// Exceptions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thrown when the agent cannot complete — max iterations exceeded, Ollama
/// returned no response, or the query is fundamentally unanswerable.
/// Distinct from tool failures (which are returned as ToolResult.Fail and
/// fed back to the model) — this exception escapes the agent entirely.
/// </summary>
public sealed class AgentException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Thrown internally during structured output validation.
/// Caught by ParseFinalResponseAsync's retry loop — never escapes the agent.
/// </summary>
internal sealed class AgentResponseValidationException(string message)
    : Exception(message);


// ─────────────────────────────────────────────────────────────────────────────
// DI Registration
// ─────────────────────────────────────────────────────────────────────────────

public static class AgentServiceExtensions
{
    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AgentOptions>()
            .Bind(configuration.GetSection(AgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();
        services.AddSingleton<Agent>();

        return services;
    }
}