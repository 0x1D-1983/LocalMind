using System.ComponentModel.DataAnnotations;

namespace LocalMind.Ingestion;

/// <summary>Qdrant collection and embedding shape used by the ingest console.</summary>
public sealed class DocumentIngestOptions
{
    public const string SectionName = "DocumentIngest";

    /// <summary>Qdrant collection name for document chunks.</summary>
    [Required]
    public string CollectionName { get; set; } = "knowledge";

    /// <summary>Ollama model name for the embedding model.</summary>
    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Vector size for the embedding model (e.g. 768 for <c>nomic-embed-text</c>).</summary>
    [Range(1, 8192)]
    public uint EmbeddingDimensions { get; set; } = 768;

    /// <summary>Target size for sub-chunks inside a long paragraph.</summary>
    [Range(1, 8192)]
    public int ChunkSize { get; set; } = 480;

    /// <summary>Larger overlap keeps facts (e.g. names in one sentence) duplicated across adjacent vectors for better recall.</summary>
    [Range(1, 8192)]
    public int Overlap { get; set; } = 160;
}
