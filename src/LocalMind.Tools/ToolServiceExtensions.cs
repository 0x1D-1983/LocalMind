using Microsoft.Extensions.DependencyInjection;
using LocalMind.Ollama;
using LocalMind.Qdrant;

namespace LocalMind.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// DI Registration
// ─────────────────────────────────────────────────────────────────────────────

public static class ToolServiceExtensions
{
    /// <summary>
    /// Registers the tool infrastructure.
    /// Add individual ITool implementations via AddTool&lt;T&gt;() after calling this.
    ///
    /// Usage in Program.cs:
    ///   builder.Services
    ///       .AddToolInfrastructure()
    ///       .AddTool&lt;KnowledgeSearchTool&gt;()
    ///       .AddTool&lt;DatabaseQueryTool&gt;()
    ///       .AddTool&lt;CalculatorTool&gt;();
    /// </summary>
    public static IServiceCollection AddToolInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<ToolManifestBuilder>();
        services.AddSingleton<IOllamaApiClientFactory, OllamaApiClientFactory>();
        services.AddSingleton<IQdrantClientFactory, QdrantClientFactory>();
        return services;
    }

    /// <summary>Registers a tool implementation by its ITool interface.</summary>
    public static IServiceCollection AddTool<T>(this IServiceCollection services)
        where T : class, ITool
    {
        // Register as both ITool (for IEnumerable<ITool> injection into ToolRegistry)
        // and as T (for direct injection if needed).
        services.AddSingleton<ITool, T>();
        services.AddSingleton<T>(sp => (T)sp.GetServices<ITool>().OfType<T>().First());
        return services;
    }
}