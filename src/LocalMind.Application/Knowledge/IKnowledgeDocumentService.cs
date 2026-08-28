namespace LocalMind.Application.Knowledge;

public interface IKnowledgeDocumentService
{
    Task<IngestDocumentResponse> IngestAsync(IngestDocumentRequest request, CancellationToken ct = default);
}
