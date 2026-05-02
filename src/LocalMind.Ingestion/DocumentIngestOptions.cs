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
}
