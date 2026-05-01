// ─────────────────────────────────────────────────────────────────────────────
// Knowledge base search (Qdrant + same embedding model as ingestion)
// ─────────────────────────────────────────────────────────────────────────────

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace LocalMind.Tools;

public sealed class KnowledgeSearchTool(
    OllamaApiClient ollama,
    QdrantClient qdrant,
    ILogger<KnowledgeSearchTool> logger) : ITool
{
    private const string CollectionName = "knowledge";
    private const string EmbedModel = "nomic-embed-text";

    public string Name => "search_knowledge_base";

    public string Description => """
        Searches the local documentation knowledge base using semantic similarity.
        Use when the user asks about architecture, processes, or concepts found in docs.
        Do NOT use for live data, counts, or anything requiring a database lookup.
        Returns the top matching document chunks with their source file names.
        """;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Natural language search query"
            },
            ["top_k"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Number of results to return. Default: 3, max: 10."
            }
        },
        ["required"] = new JsonArray { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (!input.TryGetPropertyValue("query", out var queryNode) || queryNode is not JsonValue queryVal)
        {
            sw.Stop();
            return ToolResult.Fail(Name, "Missing or invalid 'query' argument.", sw.Elapsed);
        }

        var queryText = queryVal.GetValue<string>();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            sw.Stop();
            return ToolResult.Fail(Name, "Query must be a non-empty string.", sw.Elapsed);
        }

        var topK = 3;
        if (input.TryGetPropertyValue("top_k", out var topKNode) && topKNode is JsonValue topKVal)
        {
            if (topKVal.TryGetValue(out int i))
                topK = Math.Clamp(i, 1, 10);
            else if (topKVal.TryGetValue(out long l))
                topK = Math.Clamp((int)l, 1, 10);
        }

        try
        {
            var embed = await ollama.EmbedAsync(
                new EmbedRequest { Model = EmbedModel, Input = [queryText] },
                ct);

            var vector = embed.Embeddings[0];
            var hits = await qdrant.SearchAsync(CollectionName, vector, limit: (ulong)topK, cancellationToken: ct);

            var results = hits.Select(h => new
            {
                score = h.Score,
                source = PayloadString(h.Payload, "source"),
                text = PayloadString(h.Payload, "text"),
            }).ToList();

            sw.Stop();
            var json = JsonSerializer.Serialize(results);
            logger.LogDebug("Knowledge search returned {Count} hit(s) for query length {Len}", results.Count, queryText.Length);
            return ToolResult.Ok(Name, json, sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "Knowledge search failed");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
    }

    private static string PayloadString(IReadOnlyDictionary<string, Value> payload, string key) =>
        payload.TryGetValue(key, out var v) ? v.StringValue : "";
}
