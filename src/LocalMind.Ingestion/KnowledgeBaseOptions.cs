using System.ComponentModel.DataAnnotations;

namespace LocalMind.Ingestion;

/// <summary>
/// Contract for the Qdrant vector index: every producer (ingest) and consumer (semantic search)
/// must use the same collection, embedding model, and vector width.
/// </summary>
public sealed class KnowledgeBaseOptions
{
    public const string SectionName = "KnowledgeBase";

    /// <summary>Qdrant collection name for document chunks.</summary>
    [Required]
    public string CollectionName { get; set; } = "knowledge";

    /// <summary>Ollama embedding model used to build and query vectors in this collection.</summary>
    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>Vector size for the embedding model (e.g. 768 for <c>nomic-embed-text</c>).</summary>
    [Range(1, 8192)]
    public uint VectorSize { get; set; } = 768;

    /// <summary>Default number of chunks <c>search_knowledge_base</c> returns when the model omits <c>top_k</c>.</summary>
    [Range(1, 100)]
    public int DefaultTopK { get; set; } = 5;

    /// <summary>Upper bound for <c>top_k</c> on knowledge search.</summary>
    [Range(1, 100)]
    public int MaxTopK { get; set; } = 10;
}
