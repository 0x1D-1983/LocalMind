using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalMind.Cache;

public class SemanticCacheInitializer<T>(SemanticCache<T> cache, ILogger<SemanticCacheInitializer<T>> logger) : IHostedService
{
    public Task StartAsync(CancellationToken ct)
    {
        logger.LogInformation("Initializing semantic cache...");
        return cache.EnsureCreatedAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}