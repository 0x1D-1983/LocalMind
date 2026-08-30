using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LocalMind.Ingestion;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace LocalMind.Tools;

public sealed class KnowledgeSearchTool(
    ILogger<KnowledgeSearchTool> logger,
    IOptions<KnowledgeBaseOptions> knowledgeBase,
    OllamaApiClient ollama,
    QdrantClient qdrant
    ) : ITool
{
    public string Name => "search_knowledge_base";

    public string Description => """
        Semantic search over ingested documentation (markdown and internal docs), including newly added files.
        Returns ranked chunks with file paths, short file names, and text excerpts.
        Shape the query as "<subject> <attribute> <related terms>" — avoid vague single-word queries
        like "parents" when the subject is implied.
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
                ["description"] = $"Number of chunks to return. Default: {knowledgeBase.Value.DefaultTopK}, max: {knowledgeBase.Value.MaxTopK}. Use a higher top_k for narrow factual questions (names, dates, relationships) so the right passage is not buried under generic mentions of the same topic."
            }
        },
        ["required"] = new JsonArray { "query" }
    };

    public async Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        if (!input.TryGetPropertyValue("query", out var queryNode) || queryNode is not JsonValue queryVal)
        {
            return ToolResult.Fail(Name, "Missing or invalid 'query' argument.", sw.Elapsed);
        }

        var queryText = queryVal.GetValue<string>();
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return ToolResult.Fail(Name, "Query must be a non-empty string.", sw.Elapsed);
        }

        var maxK = Math.Max(1, knowledgeBase.Value.MaxTopK);
        var topK = Math.Clamp(knowledgeBase.Value.DefaultTopK, 1, maxK);
        if (input.TryGetPropertyValue("top_k", out var topKNode) && topKNode is JsonValue topKVal)
        {
            if (topKVal.TryGetValue(out int i))
                topK = Math.Clamp(i, 1, maxK);
            else if (topKVal.TryGetValue(out long l))
                topK = Math.Clamp((int)l, 1, maxK);
            else if (topKVal.TryGetValue(out double d))
                topK = Math.Clamp((int)Math.Round(d), 1, maxK);
        }

        try
        {
            logger.LogDebug("Knowledge search: query={Query} topK={TopK}", queryText, topK);
            var embed = await ollama.EmbedAsync(
                new EmbedRequest 
                {
                    Model = knowledgeBase.Value.EmbeddingModel,
                    Input = [$"search_query: {queryText}"]  // Nomic's designed asymmetry
                },
                ct);

            if (embed?.Embeddings is not { Count: > 0 })
            {
                return ToolResult.Fail(Name, "Ollama returned no embeddings.", sw.Elapsed);
            }

            // Request payload explicitly — default selector can omit payload fields, which makes chunks look empty to the model.
            var hits = await qdrant.SearchAsync(
                knowledgeBase.Value.CollectionName,
                embed.Embeddings[0],
                limit: (ulong)topK,
                payloadSelector: true,
                vectorsSelector: false,
                cancellationToken: ct);

            var results = hits.Select(h =>
            {
                var sourcePath = PayloadString(h.Payload, "source");
                var filename = PayloadString(h.Payload, "filename");
                if (string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(sourcePath))
                    filename = Path.GetFileName(sourcePath);

                var contextualized = PayloadString(h.Payload, "contextualized_text");
                var original = PayloadString(h.Payload, "text");

                return new SearchHit(
                    Score: h.Score,
                    Source: sourcePath,
                    Filename: filename,
                    ChunkIndex: PayloadLong(h.Payload, "chunk_index"),
                    Text: string.IsNullOrWhiteSpace(contextualized) ? original : contextualized
                );
            }).ToList();

            var json = JsonSerializer.Serialize(results);
            
            if (results.Count > 0)
            {
                logger.LogInformation(
                    "Knowledge search: {Count} chunk(s); top filename={File} score={Score:F4}",
                    results.Count,
                    results[0].Filename,
                    results[0].Score);
            }
            else
                logger.LogWarning("Knowledge search returned no hits for query (length {Len})", queryText.Length);
            return ToolResult.Ok(Name, json, sw.Elapsed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Knowledge search failed");
            return ToolResult.Fail(Name, ex.Message, sw.Elapsed);
        }
        finally
        {
            sw.Stop();
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

file sealed record SearchHit(
    [property: JsonPropertyName("score")] float Score,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("chunk_index")] long ChunkIndex,
    [property: JsonPropertyName("text")] string Text);