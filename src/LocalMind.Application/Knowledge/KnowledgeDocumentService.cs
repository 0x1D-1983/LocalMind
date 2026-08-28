using LocalMind.Ingestion;

namespace LocalMind.Application.Knowledge;

public sealed class KnowledgeDocumentService(DocumentIngester ingester) : IKnowledgeDocumentService
{
    public async Task<IngestDocumentResponse> IngestAsync(
        IngestDocumentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("File name is required.", nameof(request));

        var result = await ingester.IngestTextAsync(
            request.FileName.Trim(),
            request.Content ?? "",
            source: string.IsNullOrWhiteSpace(request.Source) ? null : request.Source.Trim(),
            ct: ct);

        return new IngestDocumentResponse
        {
            FileName = result.FileName,
            ChunkCount = result.ChunkCount,
            CollectionName = result.CollectionName
        };
    }
}
