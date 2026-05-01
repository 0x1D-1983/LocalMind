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
        Prefer search queries that name the subject plus the fact you need (e.g. "Jean Grey parents family John Elaine"
        for questions about who someone's parents are — avoid overly vague single-word queries like "parents" alone).
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
                ["description"] = "Number of chunks to return. Default: 15, max: 25. Use 18–25 for narrow factual questions (names, dates, relationships) so the right passage is not buried under generic mentions of the same topic."
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

        const int maxK = 25;
        var topK = 15;
        if (input.TryGetPropertyValue("top_k", out var topKNode) && topKNode is JsonValue topKVal)
        {
            if (topKVal.TryGetValue(out int i))
                topK = Math.Clamp(i, 1, maxK);
            else if (topKVal.TryGetValue(out long l))
                topK = Math.Clamp((int)l, 1, maxK);
        }

        try
        {
            // For relationship / biography-style questions, enrich the embedding only (not the logged query) so vectors
            // sit closer to prose like "daughter of John and Elaine" when the user says "parents" or "who were".
            var embedQuery = ShouldExpandEmbeddingQuery(queryText)
                ? $"{queryText}\nbiography family parents relatives early life background"
                : queryText;

            var embed = await ollama.EmbedAsync(
                new EmbedRequest { Model = EmbedModel, Input = [embedQuery] },
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

    private static bool ShouldExpandEmbeddingQuery(string query)
    {
        var s = query.ToLowerInvariant();
        return s.Contains("parent") || s.Contains("mother") || s.Contains("father")
            || s.Contains("family") || s.Contains("sibling") || s.Contains("relative")
            || s.Contains("who was") || s.Contains("who were") || s.Contains("who is")
            || s.Contains("who are") || s.Contains("born") || s.Contains("child of")
            || s.Contains("daughter of") || s.Contains("son of");
    }
}
