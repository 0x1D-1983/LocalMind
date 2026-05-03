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
    public async Task IngestAsync(string filePath, CancellationToken ct = default)
    {
        if (!await qdrant.CollectionExistsAsync(knowledgeBase.CollectionName, cancellationToken: ct))
        {
            logger.LogInformation("Creating Qdrant collection {CollectionName}", knowledgeBase.CollectionName);
            await qdrant.CreateCollectionAsync(knowledgeBase.CollectionName, new VectorParams {
                Size = knowledgeBase.EmbeddingDimensions,
                Distance = Distance.Cosine
            }, cancellationToken: ct);
        }

        // Always ensure indexes exist — idempotent, safe to call every time
        await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "filename", PayloadSchemaType.Keyword, cancellationToken: ct);
        await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "source", PayloadSchemaType.Keyword, cancellationToken: ct);

        var text = await File.ReadAllTextAsync(filePath, cancellationToken: ct);
        var chunks = ChunkByParagraphs(text, chunkOpts.ChunkSize, chunkOpts.Overlap).ToList();
        var docLabel = Path.GetFileName(filePath);

        // 1 call to Ollama for all chunks
        var inputs = chunks.Select(c => $"search_document: {c}").ToList();
        var allEmbeddings = new List<float[]>();

        foreach (var inputBatch in inputs.Chunk(chunkOpts.EmbeddingBatchSize))
        {
            var response = await ollama.EmbedAsync(new EmbedRequest {
                Model = knowledgeBase.EmbeddingModel,
                Input = inputBatch.ToList()
            }, cancellationToken: ct);

            allEmbeddings.AddRange(response.Embeddings);
        }

        var points = chunks.Select((chunk, index) => new PointStruct {
            Id = new PointId { Uuid = GuidHelper.CreateDeterministicGuid(filePath, index).ToString() },
            Vectors = allEmbeddings[index],  // aligned by index
            Payload = {
                ["source"] = filePath,
                ["filename"] = docLabel,
                ["chunk_index"] = index,
                ["chunk_total"] = chunks.Count,
                ["text"] = chunk,
            }
        }).ToList();

        // Single round-trip to Qdrant
        logger.LogInformation("Ingesting {File}: {ChunkCount} chunks into {Collection}", docLabel, chunks.Count, knowledgeBase.CollectionName);
        
        foreach (var batch in points.Chunk(chunkOpts.UpsertBatchSize))
            await qdrant.UpsertAsync(knowledgeBase.CollectionName, batch, cancellationToken: ct);

        logger.LogInformation("Ingested {File}: {ChunkCount} chunks into {Collection}", docLabel, chunks.Count, knowledgeBase.CollectionName);
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
            var end = Math.Min(i + size, paragraph.Length);

            // Snap end to nearest word boundary (look back up to 50 chars)
            if (end < paragraph.Length)
            {
                var boundary = paragraph.LastIndexOf(' ', end, Math.Min(end - i, 50));
                if (boundary > i) end = boundary;
            }

            var slice = paragraph[i..end].Trim();
            if (slice.Length > 0)
                yield return slice;

            if (end >= paragraph.Length)
                yield break;
        }
    }
}
