using System.Text.Json;
using LocalMind.Telemetry;
using OllamaSharp;
using OllamaSharp.Models;

namespace LocalMind.Cache;

public class EntityExtractor(OllamaApiClient ollama, EntityExtractorOptions options)
{
    public async Task<IReadOnlyList<string>> ExtractAsync(
        string query, CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Cache.StartActivity("cache.extract_entities");
        var response = await ollama.GenerateAsync(new GenerateRequest {
            Model  = options.Model,
            Prompt = $"""
                Extract all named entities (people, places, organisations, events)
                from the following query. Return them as a JSON array of strings.
                Return an empty array if there are none.
                Return only the JSON array, no explanation.

                Query: {query}
                """,
            Stream = false
        }, ct).LastAsync(ct);

        return JsonSerializer.Deserialize<List<string>>(response?.Response.Trim() ?? "") ?? [];
    }
}