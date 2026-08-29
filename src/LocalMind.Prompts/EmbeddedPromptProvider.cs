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
        var match = FindResource(name, version);
        if (match is null)
        {
            logger.LogError(
                "Prompt '{PromptName}' version '{PromptVersion}' was not found. Embedded resources: {Resources}",
                name,
                version ?? "latest",
                string.Join(", ", _assembly.GetManifestResourceNames()));
            throw new PromptNotFoundException(name, version);
        }

        using var stream = _assembly.GetManifestResourceStream(match.ResourceName)
            ?? throw new PromptNotFoundException(name, version);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd().Trim();

        if (content.Length == 0)
            throw new PromptNotFoundException(name, version);

        return new Prompt
        {
            Name = name,
            Version = match.Version,
            Content = content
        };
    }

    private ResourceMatch? FindResource(string name, string? version)
    {
        var candidates = _assembly.GetManifestResourceNames()
            .Select(resource => TryParse(resource, name))
            .OfType<ResourceMatch>()
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (string.IsNullOrWhiteSpace(version)
            || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            return candidates.MaxBy(c => c.Number);
        }

        var requested = NormalizeVersion(version);
        return requested is null
            ? null
            : candidates.FirstOrDefault(c =>
                c.Version.Equals(requested, StringComparison.OrdinalIgnoreCase));
    }

    private static ResourceMatch? TryParse(string resourceName, string promptName)
    {
        var resource = resourceName.Replace('\\', '/');
        var names = new[] { promptName, promptName.Replace('-', '_') }
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var slashMarker = $"/{name}/v";
            var slashIndex = resource.LastIndexOf(slashMarker, StringComparison.OrdinalIgnoreCase);
            if (slashIndex >= 0
                && TryParseVersionFile(resource[(slashIndex + slashMarker.Length)..], out var slashVersion))
            {
                return new ResourceMatch(resourceName, slashVersion.Version, slashVersion.Number);
            }

            var dotMarker = $".{name}.v";
            var dotIndex = resourceName.LastIndexOf(dotMarker, StringComparison.OrdinalIgnoreCase);
            if (dotIndex >= 0
                && TryParseVersionFile(resourceName[(dotIndex + dotMarker.Length)..], out var dotVersion))
            {
                return new ResourceMatch(resourceName, dotVersion.Version, dotVersion.Number);
            }
        }

        return null;
    }

    private static bool TryParseVersionFile(string rest, out (string Version, int Number) parsed)
    {
        parsed = default;
        if (!rest.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return false;

        var numberPart = rest[..^4];
        if (!int.TryParse(numberPart, out var number) || number < 1)
            return false;

        parsed = ($"v{number}", number);
        return true;
    }

    private static string? NormalizeVersion(string version)
    {
        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[1..];

        return int.TryParse(trimmed, out var number) && number >= 1
            ? $"v{number}"
            : null;
    }

    private static string CacheKey(string name, string? version) =>
        $"{name}:{version ?? "latest"}";

    private sealed record ResourceMatch(string ResourceName, string Version, int Number);
}
