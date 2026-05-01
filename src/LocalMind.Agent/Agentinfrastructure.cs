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
    /// <summary>
    /// Registers the Agent and its options.
    ///
    /// Full wiring in Program.cs:
    ///
    ///   var builder = WebApplication.CreateBuilder(args);  // or Host.CreateApplicationBuilder
    ///
    ///   builder.Services
    ///       // Ollama client
    ///       .AddSingleton(_ => new OllamaApiClient(new Uri("http://localhost:11434")))
    ///
    ///       // Tool layer (from previous phase)
    ///       .AddToolInfrastructure()
    ///       .AddTool&lt;KnowledgeSearchTool&gt;()
    ///       .AddTool&lt;DatabaseQueryTool&gt;()
    ///       .AddTool&lt;CalculatorTool&gt;()
    ///
    ///       // Cache (Phase 4)
    ///       .AddSingleton&lt;SemanticCache&gt;()
    ///
    ///       // Agent
    ///       .AddAgent(builder.Configuration);
    ///
    /// </summary>
    public static IServiceCollection AddAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentOptions>(
            configuration.GetSection(AgentOptions.SectionName));

        services.AddSingleton<IStructuredOutputParser, StructuredOutputParser>();
        services.AddSingleton<IOllamaApiClientFactory, OllamaApiClientFactory>();

        services.AddSingleton<Agent>();

        return services;
    }
}