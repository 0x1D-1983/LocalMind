using LocalMind.Ingestion;
using LocalMind.Telemetry;

namespace LocalMind.Application.Knowledge;

public sealed class KnowledgeDocumentService(DocumentIngester ingester) : IKnowledgeDocumentService
{
    public async Task<IngestDocumentResponse> IngestAsync(
        IngestDocumentRequest request,
        CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Application.StartActivity("knowledge.ingest");
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.FileName))
                throw new ArgumentException("File name is required.", nameof(request));

            activity?.SetTag("document.file_name", request.FileName);

            var result = await ingester.IngestTextAsync(
                request.FileName.Trim(),
                request.Content ?? "",
                source: string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
                ct: ct);

            activity?.SetTag("document.chunk_count", result.ChunkCount);
            activity?.SetTag("document.collection", result.CollectionName);

            return new IngestDocumentResponse
            {
                FileName = result.FileName,
                ChunkCount = result.ChunkCount,
                CollectionName = result.CollectionName
            };
        }
        catch (Exception ex)
        {
            activity.RecordError(ex);
            throw;
        }
    }
}
