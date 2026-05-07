using Microsoft.Extensions.DependencyInjection;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Microsoft.Extensions.Options;

namespace LocalMind.Tools;

public static class ToolServiceExtensions
{
    public static IServiceCollection AddToolInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CharacterRosterOptions>()
            .Bind(configuration.GetSection(CharacterRosterOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
        {
            var cs = sp.GetRequiredService<IOptions<CharacterRosterOptions>>().Value.ConnectionString;

            return NpgsqlDataSource.Create(cs);
        });

        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<ToolManifestBuilder>();

        return services;
    }

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