using System.ComponentModel.DataAnnotations;

namespace LocalMind.Ingestion;

/// <summary>Chunking hyperparameters for the ingest pipeline only (not used by the chat agent).</summary>
public sealed class DocumentIngestOptions
{
    public const string SectionName = "DocumentIngest";

    /// <summary>Target size for sub-chunks inside a long paragraph.</summary>
    [Range(1, 8192)]
    public int ChunkSize { get; set; } = 480;

    /// <summary>Larger overlap keeps facts (e.g. names in one sentence) duplicated across adjacent vectors for better recall.</summary>
    [Range(1, 8192)]
    public int Overlap { get; set; } = 160;

    /// <summary>Batch size for embedding requests to Ollama.</summary>
    [Range(1, 128)]
    public int EmbeddingBatchSize { get; set; } = 16;

    /// <summary>Batch size for upsert requests to Qdrant.</summary>
    [Range(1, 128)]
    public int UpsertBatchSize { get; set; } = 32;

    public bool EnableContextualization { get; set; } = true;

    /// <summary>
    /// Chat-capable Ollama model used to write a short disambiguating preamble per chunk.
    /// Must support chat — not an embedding model such as nomic-embed-text.
    /// </summary>
    [Required]
    public string ContextualizationModel { get; set; } = "qwen2.5:1.5b-instruct";

    /// <summary>
    /// Bounded parallelism for the chat calls. Local Ollama chat is much slower
    /// than the embedding batch call, so keep this modest (2–4) unless you have
    /// spare GPU capacity.
    /// </summary>
    [Range(1, 32)]
    public int ContextualizationConcurrency { get; set; } = 3;
    
}
