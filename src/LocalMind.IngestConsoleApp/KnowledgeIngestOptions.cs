namespace LocalMind.IngestConsoleApp;

/// <summary>Qdrant collection and embedding shape used by the ingest console.</summary>
public sealed class KnowledgeIngestOptions
{
    public const string SectionName = "KnowledgeIngest";

    /// <summary>Qdrant collection name for document chunks.</summary>
    public string CollectionName { get; set; } = "knowledge";

    /// <summary>Vector size for the embedding model (e.g. 768 for <c>nomic-embed-text</c>).</summary>
    public uint EmbeddingDimensions { get; set; } = 768;
}
