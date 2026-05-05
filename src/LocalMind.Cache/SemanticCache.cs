using System.Text.Json;
using OllamaSharp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OllamaSharp.Models;

namespace LocalMind.Cache;

public class SemanticCache<T>(
    OllamaApiClient ollama,
    QdrantClient qdrant,
    EntityExtractor entityExtractor,
    SemanticCacheOptions options)
{
    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        var collections = await qdrant.ListCollectionsAsync(ct);

        if (collections.Any(c => c == options.CollectionName))
            return;

        await qdrant.CreateCollectionAsync(options.CollectionName, new VectorParams {
                Size = options.VectorSize,
                Distance = Distance.Cosine
            }, cancellationToken: ct);
    }

    public async Task<CacheResult<T>> GetAsync(string query, CancellationToken ct = default)
    {
        var entities = await entityExtractor.ExtractAsync(query, ct);
        var embedding = await ollama.EmbedAsync(new EmbedRequest {
            Model = options.EmbeddingModel,
            Input = [query]
        }, cancellationToken: ct);

        Filter? filter = entities.Any()
            ? CreateFilter(entities)
            : null;

        var results = await qdrant.SearchAsync(
            options.CollectionName,
            embedding.Embeddings[0].ToArray(),
            filter: filter,
            limit: 1,
            scoreThreshold: options.SimilarityThreshold,
            cancellationToken: ct);

        if (!results.Any())
            return CacheResult<T>.Miss();

        var payload = results.First().Payload;
        var cachedAt = DateTimeOffset.FromUnixTimeSeconds(payload["cached_at"].IntegerValue);
        var value = JsonSerializer.Deserialize<T>(payload["response"].StringValue)!;

        return CacheResult<T>.Hit(value, cachedAt);
    }

    public async Task SetAsync(string query, T value, CancellationToken ct = default)
    {
        var entities = await entityExtractor.ExtractAsync(query, ct);
        var embedding = await ollama.EmbedAsync(new EmbedRequest {
            Model = options.EmbeddingModel,
            Input = [query]
        }, cancellationToken: ct);

        await qdrant.UpsertAsync(options.CollectionName, [
            new PointStruct {
                Id      = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = embedding.Embeddings[0].ToArray(),
                Payload = {
                    ["query"] = query,
                    ["response"] = JsonSerializer.Serialize(value),
                    ["cached_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    ["entities"] = entities.ToArray()
                }
            }
        ], cancellationToken: ct);
    }

    private Filter? CreateFilter(IReadOnlyList<string> entities)
    {
        // Only apply filter when the query has named entities
        var conditions = entities.Select(e => new Condition {
            Field = new FieldCondition {
                Key = "entities",
                Match = new Match { Keyword = e }
            }}).ToList();

        Condition? combinedConditions = default;
        if (conditions.Count >= 2)
        {
            combinedConditions = conditions[0] & conditions[1];
            for (int i = 2; i < conditions.Count; i++)
            {
                combinedConditions &= conditions[i];
            }
        }
        else if (conditions.Count == 1)
        {
            combinedConditions = conditions[0];
        }

        return combinedConditions is not null
            ? new Filter(combinedConditions)
            : null;
    }
}