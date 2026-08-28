using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace LocalMind.Prompts;

public sealed class EmbeddedPromptProvider(ILogger<EmbeddedPromptProvider> logger) : IPromptProvider
{
    private readonly Assembly _assembly = typeof(EmbeddedPromptProvider).Assembly;
    private readonly ConcurrentDictionary<string, Prompt> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<Prompt> GetAsync(
        string name,
        string? version = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var cacheKey = CacheKey(name, version);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return Task.FromResult(cached);

        var prompt = Load(name, version);
        _cache[cacheKey] = prompt;
        logger.LogDebug(
            "Loaded prompt {PromptName} version {PromptVersion} ({Length} chars)",
            prompt.Name, prompt.Version, prompt.Content.Length);

        return Task.FromResult(prompt);
    }

    private Prompt Load(string name, string? version)
    {
        var resourceName = FindResource(name, version)
            ?? throw new PromptNotFoundException(name, version);

        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new PromptNotFoundException(name, version);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd().Trim();

        if (content.Length == 0)
            throw new PromptNotFoundException(name, version);

        return new Prompt
        {
            Name = name,
            Version = string.IsNullOrWhiteSpace(version) ? "latest" : version.Trim(),
            Content = content
        };
    }

    private string? FindResource(string name, string? version)
    {
        var names = _assembly.GetManifestResourceNames();
        if (string.IsNullOrWhiteSpace(version)
            || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return names.FirstOrDefault(n =>
                n.EndsWith($".{name}.md", StringComparison.OrdinalIgnoreCase));
        }

        return names.FirstOrDefault(n =>
            n.EndsWith($".{name}.{version}.md", StringComparison.OrdinalIgnoreCase));
    }

    private static string CacheKey(string name, string? version) =>
        $"{name}:{version ?? "latest"}";
}
