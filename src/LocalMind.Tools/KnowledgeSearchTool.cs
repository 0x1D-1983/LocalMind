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
        Searches the ingested documentation knowledge base (Qdrant) using semantic similarity.
        Call this tool whenever the user asks about facts, topics, people, events, or wording that could appear
        in uploaded markdown or internal docs — including "new" files they just added.
        You may call it multiple times with different queries if the first results are thin.
        Prefer a focused search query (key terms from the user's question).
        Do NOT use for live database metrics or real-time counts unless the user clearly needs DB data.
        Returns ranked chunks with file paths, short file names, and text excerpts.
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
                ["description"] = "Number of chunks to return. Default: 8, max: 10. Use 8–10 when searching for newer or niche docs so they are not pushed out of the top results."
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

        var topK = 8;
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
            // Request payload explicitly — default selector can omit payload fields, which makes chunks look empty to the model.
            var hits = await qdrant.SearchAsync(
                CollectionName,
                vector,
                limit: (ulong)topK,
                payloadSelector: true,
                vectorsSelector: false,
                cancellationToken: ct);

            var results = hits.Select(h =>
            {
                var sourcePath = PayloadString(h.Payload, "source");
                return new
                {
                    score = h.Score,
                    source = sourcePath,
                    file = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetFileName(sourcePath),
                    chunk_index = PayloadLong(h.Payload, "chunk_index"),
                    text = PayloadString(h.Payload, "text"),
                };
            }).ToList();

            sw.Stop();
            var json = JsonSerializer.Serialize(results);
            if (results.Count > 0)
            {
                logger.LogInformation(
                    "Knowledge search: {Count} chunk(s); top file={File} score={Score:F4}",
                    results.Count,
                    results[0].file,
                    results[0].score);
            }
            else
                logger.LogWarning("Knowledge search returned no hits for query (length {Len})", queryText.Length);
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

    private static long PayloadLong(IReadOnlyDictionary<string, Value> payload, string key)
    {
        if (!payload.TryGetValue(key, out var v))
            return -1;
        if (v.HasIntegerValue)
            return v.IntegerValue;
        if (v.HasDoubleValue)
            return (long)v.DoubleValue;
        return -1;
    }
}
