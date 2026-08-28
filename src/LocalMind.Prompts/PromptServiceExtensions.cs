using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Prompts;

public static class PromptServiceExtensions
{
    public static IServiceCollection AddPrompts(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(IPromptProvider)))
            return services;

        services.AddSingleton<IPromptProvider, EmbeddedPromptProvider>();
        return services;
    }
}
