using System.Text.Json;
using OllamaSharp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using OllamaSharp.Models;

namespace LocalMind.Cache;

public class SemanticCache<T>(
    OllamaApiClient ollama,
    QdrantClient qdrant,
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
        var embedding = await ollama.EmbedAsync(new EmbedRequest {
            Model = options.EmbeddingModel,
            Input = [query]
        }, cancellationToken: ct);

        var results = await qdrant.SearchAsync(
            options.CollectionName,
            embedding.Embeddings[0].ToArray(),
            limit: 1,
            scoreThreshold: options.SimilarityThreshold,
            cancellationToken: ct);

        if (!results.Any())
            return CacheResult<T>.Miss();

        var payload   = results.First().Payload;
        var cached    = payload["response"].StringValue;
        var cachedAt  = DateTimeOffset.FromUnixTimeSeconds(payload["cached_at"].IntegerValue);
        var value     = JsonSerializer.Deserialize<T>(cached)!;

        return CacheResult<T>.Hit(value, cachedAt);
    }

    public async Task SetAsync(string query, T value, CancellationToken ct = default)
    {
        var embedding = await ollama.EmbedAsync(new EmbedRequest {
            Model = options.EmbeddingModel,
            Input = [query]
        }, cancellationToken: ct);

        await qdrant.UpsertAsync(options.CollectionName, [
            new PointStruct {
                Id      = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = embedding.Embeddings[0].ToArray(),
                Payload = {
                    ["query"]     = query,
                    ["response"]  = JsonSerializer.Serialize(value),
                    ["cached_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }
            }
        ], cancellationToken: ct);
    }
}