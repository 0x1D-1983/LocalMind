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
        Searches the ingested documentation knowledge base (Qdrant) using semantic similarity.
        Call this tool whenever the user asks about facts, topics, people, events, or wording that could appear
        in uploaded markdown or internal docs — including "new" files they just added.
        You may call it multiple times with different queries if the first results are thin.
        Prefer search queries shaped as "<subject> <attribute> <related terms>" (subject, what you need to know,
        and disambiguating context) — avoid overly vague single-word queries like "parents" alone when the subject is implied.
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
                sw.Stop();
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

                return new SearchHit(
                    Score: h.Score,
                    Source: sourcePath,
                    Filename: filename,
                    ChunkIndex: PayloadLong(h.Payload, "chunk_index"),
                    Text: PayloadString(h.Payload, "text")
                );
            }).ToList();

            sw.Stop();
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

public sealed record SearchHit(
    [property: JsonPropertyName("score")] float Score,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("chunk_index")] long ChunkIndex,
    [property: JsonPropertyName("text")] string Text);