using System.Text.Json;
using LocalMind.Telemetry;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;

namespace LocalMind.Cache;

public class EntityExtractor(
    OllamaApiClient ollama,
    EntityExtractorOptions options,
    ILogger<EntityExtractor> logger)
{
    private const string EntitiesExampleJson = """{"entities":["Jean Grey"]}""";
    private const string EmptyEntitiesJson = """{"entities":[]}""";

    public async Task<IReadOnlyList<string>> ExtractAsync(
        string query, CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Cache.StartActivity("cache.extract_entities");
        var response = await ollama.GenerateAsync(new GenerateRequest {
            Model  = options.Model,
            Format = "json",
            Prompt = $"""
                Extract all named entities (people, places, organisations, events)
                from the following query.
                Return a JSON object with a single key "entities" whose value is an array of strings.
                Example: {EntitiesExampleJson}
                If there are none, return {EmptyEntitiesJson}

                Query: {query}
                """,
            Stream = false
        }, ct).LastAsync(ct);

        var raw = response?.Response ?? "";
        activity?.SetTag("cache.extract_entities.raw_length", raw.Length);

        if (TryParseEntities(raw, out var entities))
            return entities;

        logger.LogWarning(
            "Entity extractor returned non-JSON for model {Model} (first 200 chars): {Preview}",
            options.Model,
            raw.Length <= 200 ? raw : raw[..200]);
        return [];
    }

    /// <summary>
    /// Ollama <c>format=json</c> typically yields an object. Small models often
    /// emit <c>{}</c> or <c>{"entities":[...]}</c> rather than a bare array.
    /// </summary>
    private static bool TryParseEntities(string raw, out IReadOnlyList<string> entities)
    {
        entities = [];
        var json = StripMarkdownFences(raw);
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryReadEntities(doc.RootElement, out entities);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadEntities(JsonElement root, out IReadOnlyList<string> entities)
    {
        entities = [];

        if (root.ValueKind == JsonValueKind.Array)
            return TryReadStringArray(root, out entities);

        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (root.TryGetProperty("entities", out var named) && named.ValueKind == JsonValueKind.Array)
            return TryReadStringArray(named, out entities);

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Array)
                return TryReadStringArray(prop.Value, out entities);
        }

        // {} or an object with no array fields: no entities, still valid.
        return true;
    }

    private static bool TryReadStringArray(JsonElement array, out IReadOnlyList<string> entities)
    {
        var list = new List<string>();
        foreach (var el in array.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
                continue;
            var value = el.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value.Trim());
        }

        entities = list;
        return true;
    }

    private static string StripMarkdownFences(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];

            trimmed = trimmed.Trim();
        }

        if (trimmed.Length >= 2 && trimmed[0] == '`' && trimmed[^1] == '`')
            trimmed = trimmed[1..^1].Trim();

        return trimmed;
    }
}
