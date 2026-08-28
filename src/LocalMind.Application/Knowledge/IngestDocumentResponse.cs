namespace LocalMind.Application.Knowledge;

public sealed class IngestDocumentResponse
{
    public required string FileName { get; init; }
    public required int ChunkCount { get; init; }
    public required string CollectionName { get; init; }
}
