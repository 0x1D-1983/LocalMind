using System.Text.RegularExpressions;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Logging;

namespace LocalMind.Ingestion;

public class DocumentIngester(
    OllamaApiClient ollama,
    QdrantClient qdrant,
    KnowledgeBaseOptions knowledgeBase,
    DocumentIngestOptions chunkOpts,
    ILogger<DocumentIngester> logger)
{
    public async Task IngestAsync(string filePath)
    {
        if (!await qdrant.CollectionExistsAsync(knowledgeBase.CollectionName))
        {
            logger.LogInformation("Creating Qdrant collection {CollectionName}", knowledgeBase.CollectionName);
            await qdrant.CreateCollectionAsync(knowledgeBase.CollectionName, new VectorParams
            {
                Size = knowledgeBase.EmbeddingDimensions,
                Distance = Distance.Cosine
            });

            // Index the fields you'll filter on (filename and source)
            await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "filename", PayloadSchemaType.Keyword);
            await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "source", PayloadSchemaType.Keyword);
        }

        var text = await File.ReadAllTextAsync(filePath);
        var chunks = ChunkByParagraphs(text, chunkOpts.ChunkSize, chunkOpts.Overlap).ToList();
        var docLabel = Path.GetFileName(filePath);

        var points = new List<PointStruct>();

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            var embedding = await ollama.EmbedAsync(new EmbedRequest {
                Model = knowledgeBase.EmbeddingModel,
                Input = [$"search_document: {chunk}"] // Nomic's designed asymmetry
            });

            points.Add(new PointStruct {
                Id = new PointId { Uuid = GuidHelper.CreateDeterministicGuid(filePath, index).ToString() }, // create idempotent Point IDs
                Vectors = embedding.Embeddings[0],
                Payload = {
                    ["source"] = filePath,
                    ["filename"] = docLabel,
                    ["chunk_index"] = index,
                    ["chunk_total"] = chunks.Count,   // useful for context reconstruction
                    ["text"] = chunk
                }
            });
        }

        // Single round-trip to Qdrant
        const int batchSize = 32;
        foreach (var batch in points.Chunk(batchSize))
            await qdrant.UpsertAsync(knowledgeBase.CollectionName, batch);
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
