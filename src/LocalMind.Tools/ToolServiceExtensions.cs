using Microsoft.Extensions.DependencyInjection;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;

namespace LocalMind.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// DI Registration
// ─────────────────────────────────────────────────────────────────────────────

public static class ToolServiceExtensions
{
    public static IServiceCollection AddToolInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<ToolManifestBuilder>();

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