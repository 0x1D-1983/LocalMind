namespace LocalMind.Application.Knowledge;

public sealed class IngestDocumentRequest
{
    public required string FileName { get; init; }
    public required string Content { get; init; }
    public string? Source { get; init; }
}
