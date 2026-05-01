using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace LocalMind.Ingestion;

// DocumentIngester.cs
public class DocumentIngester(OllamaApiClient ollama, QdrantClient qdrant)
{
    private const int ChunkSize = 400;
    private const int Overlap = 40;

    public async Task IngestAsync(string filePath)
    {
        var text = await File.ReadAllTextAsync(filePath);
        var chunks = Chunk(text, ChunkSize, Overlap);

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            // Generate embedding
            var embedding = await ollama.EmbedAsync(
                new EmbedRequest {
                    Model = "nomic-embed-text",
                    Input = [chunk]
                });

            var vector = embedding.Embeddings[0];

            // Upsert to Qdrant
            await qdrant.UpsertAsync("knowledge", [
                new PointStruct {
                    Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                    Vectors = vector,
                    Payload = {
                        ["source"] = filePath,
                        ["chunk_index"] = index,
                        ["text"] = chunk
                    }
                }
            ]);
        }
    }

    private static IEnumerable<string> Chunk(string text, int size, int overlap)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var step = Math.Max(1, size - overlap);
        for (var i = 0; i < text.Length; i += step)
        {
            var len = Math.Min(size, text.Length - i);
            yield return text.Substring(i, len);
            if (i + len >= text.Length)
                yield break;
        }
    }
}