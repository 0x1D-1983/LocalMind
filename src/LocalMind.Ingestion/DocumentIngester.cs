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

            // Upsert to Qdrant
            await qdrant.UpsertAsync("knowledge", [
                new PointStruct {
                    Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                    Vectors = embedding.Embeddings.ToArray(),
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
        // TODO: Implement sliding window chunker
        // Stretch: chunk on sentence boundaries instead of fixed tokens
        return Enumerable.Empty<string>();
    }
}