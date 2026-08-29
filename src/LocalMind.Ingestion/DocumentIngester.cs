using System.Text.RegularExpressions;
using OllamaSharp;
using OllamaSharp.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using LocalMind.Telemetry;
using Microsoft.Extensions.Logging;

namespace LocalMind.Ingestion;

public class DocumentIngester(
    OllamaApiClient ollama,
    QdrantClient qdrant,
    KnowledgeBaseOptions knowledgeBase,
    DocumentIngestOptions chunkOpts,
    ILogger<DocumentIngester> logger)
{
    public async Task<DocumentIngestResult> IngestAsync(string filePath, CancellationToken ct = default)
    {
        var text = await File.ReadAllTextAsync(filePath, cancellationToken: ct);
        return await IngestTextAsync(Path.GetFileName(filePath), text, source: filePath, ct);
    }

    public async Task<DocumentIngestResult> IngestTextAsync(
        string fileName,
        string text,
        string? source = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        using var activity = LocalMindActivitySources.Ingestion.StartActivity("ingest.document");
        activity?.SetTag("document.file_name", fileName);

        await EnsureCollectionAsync(ct);

        var chunks = ChunkByParagraphs(text ?? "", chunkOpts.ChunkSize, chunkOpts.Overlap).ToList();
        var docLabel = Path.GetFileName(fileName);
        var sourceLabel = string.IsNullOrWhiteSpace(source) ? docLabel : source;

        var inputs = chunks.Select(c => $"search_document: {c}").ToList();
        var allEmbeddings = new List<float[]>();

        using (LocalMindActivitySources.Ingestion.StartActivity("ingest.embed"))
        {
            foreach (var inputBatch in inputs.Chunk(chunkOpts.EmbeddingBatchSize))
            {
                var response = await ollama.EmbedAsync(new EmbedRequest {
                    Model = knowledgeBase.EmbeddingModel,
                    Input = inputBatch.ToList()
                }, cancellationToken: ct);

                allEmbeddings.AddRange(response.Embeddings);
            }
        }

        var points = chunks.Select((chunk, index) => new PointStruct {
            Id = new PointId { Uuid = GuidHelper.CreateDeterministicGuid(docLabel, index).ToString() },
            Vectors = allEmbeddings[index],
            Payload = {
                ["source"] = sourceLabel,
                ["filename"] = docLabel,
                ["chunk_index"] = index,
                ["chunk_total"] = chunks.Count,
                ["text"] = chunk,
            }
        }).ToList();

        logger.LogInformation("Ingesting {File}: {ChunkCount} chunks into {Collection}", docLabel, chunks.Count, knowledgeBase.CollectionName);

        using (LocalMindActivitySources.Ingestion.StartActivity("ingest.upsert"))
        {
            foreach (var batch in points.Chunk(chunkOpts.UpsertBatchSize))
                await qdrant.UpsertAsync(knowledgeBase.CollectionName, batch, cancellationToken: ct);
        }

        activity?.SetTag("document.chunk_count", chunks.Count);
        activity?.SetTag("document.collection", knowledgeBase.CollectionName);

        logger.LogInformation("Ingested {File}: {ChunkCount} chunks into {Collection}", docLabel, chunks.Count, knowledgeBase.CollectionName);

        return new DocumentIngestResult(docLabel, chunks.Count, knowledgeBase.CollectionName);
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        if (!await qdrant.CollectionExistsAsync(knowledgeBase.CollectionName, cancellationToken: ct))
        {
            logger.LogInformation("Creating Qdrant collection {CollectionName}", knowledgeBase.CollectionName);
            await qdrant.CreateCollectionAsync(knowledgeBase.CollectionName, new VectorParams {
                Size = knowledgeBase.VectorSize,
                Distance = Distance.Cosine
            }, cancellationToken: ct);
        }

        await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "filename", PayloadSchemaType.Keyword, cancellationToken: ct);
        await qdrant.CreatePayloadIndexAsync(knowledgeBase.CollectionName, "source", PayloadSchemaType.Keyword, cancellationToken: ct);
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
