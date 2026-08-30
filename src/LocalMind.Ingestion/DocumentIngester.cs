using System.Text.RegularExpressions;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
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
    // Cap how much of the source doc we feed back in per chunk when generating context.
    // Keeps the contextualization prompt cheap even on long documents.
    private const int MaxDocContextChars = 6000;

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

        var rawText = text ?? "";
        var chunks = ChunkByParagraphs(rawText, chunkOpts.ChunkSize, chunkOpts.Overlap).ToList();
        var docLabel = Path.GetFileName(fileName);
        var sourceLabel = string.IsNullOrWhiteSpace(source) ? docLabel : source;

        // --- Contextualization pass -------------------------------------------------
        // For each chunk, ask a chat model to write 1-2 sentences that resolve
        // ambiguous references (pronouns, "the redhead", reveals like "actually this
        // was X's memory, not Y's") using the surrounding document as ground truth.
        // This is what fixes cases where the subject of the fact (e.g. Jean Grey)
        // is only named in a clause elsewhere in the chunk while the bulk of the
        // text is about someone else (e.g. Madelyne). Dense embeddings of the raw
        // chunk skew toward the dominant subject and bury the actual answer.
        var contextualizedChunks = chunks;
        if (chunkOpts.EnableContextualization)
        {
            using (LocalMindActivitySources.Ingestion.StartActivity("ingest.contextualize"))
            {
                logger.LogInformation(
                    "Contextualizing {ChunkCount} chunk(s) with {Model}",
                    chunks.Count,
                    chunkOpts.ContextualizationModel);
                contextualizedChunks = await ContextualizeChunksAsync(rawText, chunks, ct);
            }
        }

        var inputs = contextualizedChunks.Select(c => $"search_document: {c}").ToList();
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
                ["text"] = chunk,                             // original, for display/citation
                ["contextualized_text"] = contextualizedChunks[index], // what got embedded; feed THIS to the RAG generator
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

    /// <summary>
    /// Generates a short disambiguating preamble for each chunk using the chat model,
    /// then returns "{context}\n\n{chunk}" for embedding. Runs with bounded concurrency
    /// since local Ollama chat calls are much slower than the embedding batch calls.
    /// Falls back to the raw chunk on any failure so ingestion never hard-fails on this step.
    /// </summary>
    private async Task<List<string>> ContextualizeChunksAsync(string fullDocText, List<string> chunks, CancellationToken ct)
    {
        var docExcerpt = fullDocText.Length > MaxDocContextChars
            ? fullDocText[..MaxDocContextChars]
            : fullDocText;

        var results = new string[chunks.Count];
        using var gate = new SemaphoreSlim(chunkOpts.ContextualizationConcurrency);

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await gate.WaitAsync(ct);
            try
            {
                results[index] = await GenerateChunkContextAsync(docExcerpt, chunk, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Contextualization failed for chunk {Index}; falling back to raw chunk", index);
                results[index] = chunk;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    private async Task<string> GenerateChunkContextAsync(string docExcerpt, string chunk, CancellationToken ct)
    {
        var prompt = $"""
            You are preparing a passage for a search index. Given the document
            excerpt and the specific chunk below, write 1-2 short sentences of
            context that would help someone understand who/what the chunk is
            really about, especially if the chunk reveals that an event, memory,
            or trait actually belongs to a different person/entity than the one
            it initially seems to describe. Only state facts present in the
            excerpt. Do not summarize the whole chunk, just add the missing
            disambiguating context. Output only the sentences, nothing else.

            <document_excerpt>
            {docExcerpt}
            </document_excerpt>

            <chunk>
            {chunk}
            </chunk>
            """;

        var response = await ollama.ChatAsync(new ChatRequest
        {
            Model = chunkOpts.ContextualizationModel,
            Messages = [new Message { Role = ChatRole.User, Content = prompt }],
            Stream = false
        }, ct).StreamToEndAsync(); // adjust to your OllamaSharp version's non-streaming call shape

        var contextSentence = response?.Message?.Content?.Trim();

        return string.IsNullOrWhiteSpace(contextSentence)
            ? chunk
            : $"{contextSentence}\n\n{chunk}";
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