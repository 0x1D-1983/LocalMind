namespace LocalMind.Ingestion;

public sealed record DocumentIngestResult(
    string FileName,
    int ChunkCount,
    string CollectionName);
