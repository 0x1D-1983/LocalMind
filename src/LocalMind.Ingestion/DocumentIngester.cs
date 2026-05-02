using System.Text.RegularExpressions;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Logging;

namespace LocalMind.Ingestion;

public class DocumentIngester(OllamaApiClient ollama, QdrantClient qdrant, DocumentIngestOptions ingestOpts, ILogger<DocumentIngester> logger)
{
    public async Task IngestAsync(string filePath)
    {
        if (!await qdrant.CollectionExistsAsync(ingestOpts.CollectionName))
        {
            logger.LogInformation("Creating Qdrant collection {CollectionName}", ingestOpts.CollectionName);
            await qdrant.CreateCollectionAsync(ingestOpts.CollectionName, new VectorParams
            {
                Size = ingestOpts.EmbeddingDimensions,
                Distance = Distance.Cosine
            });
        }

        var text = await File.ReadAllTextAsync(filePath);
        var chunks = ChunkByParagraphs(text, ingestOpts.ChunkSize, ingestOpts.Overlap).ToList();
        var docLabel = Path.GetFileName(filePath);

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            // Prefix the document name only for the embedding so vectors align with file/topic-specific questions;
            // payload keeps the raw chunk text for the model to read.
            var embedText = $"{docLabel}\n{chunk}";

            var embedding = await ollama.EmbedAsync(
                new EmbedRequest {
                    Model = ingestOpts.EmbeddingModel,
                    Input = [embedText]
                });

            var vector = embedding.Embeddings[0];

            await qdrant.UpsertAsync(ingestOpts.CollectionName, [
                new PointStruct {
                    Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                    Vectors = vector,
                    Payload = {
                        ["source"] = filePath,
                        ["filename"] = docLabel,
                        ["chunk_index"] = index,
                        ["text"] = chunk
                    }
                }
            ]);
        }
    }

    /// <summary>
    /// Split on blank lines first so biography sections, headers, and paragraphs stay intact when possible.
    /// Long paragraphs still use a sliding window with overlap.
    /// </summary>
    private static IEnumerable<string> ChunkByParagraphs(string text, int size, int overlap)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        foreach (var paragraph in SplitParagraphs(text))
        {
            foreach (var chunk in ChunkWithinParagraph(paragraph, size, overlap))
                yield return chunk;
        }
    }

    private static IEnumerable<string> SplitParagraphs(string text)
    {
        foreach (var p in Regex.Split(text, @"\r?\n\s*\r?\n", RegexOptions.Multiline))
        {
            var t = p.Trim();
            if (t.Length > 0)
                yield return t;
        }
    }

    private static IEnumerable<string> ChunkWithinParagraph(string paragraph, int size, int overlap)
    {
        if (paragraph.Length <= size)
        {
            yield return paragraph;
            yield break;
        }

        var step = Math.Max(1, size - overlap);
        for (var i = 0; i < paragraph.Length; i += step)
        {
            var len = Math.Min(size, paragraph.Length - i);
            yield return paragraph.Substring(i, len);
            if (i + len >= paragraph.Length)
                yield break;
        }
    }


}