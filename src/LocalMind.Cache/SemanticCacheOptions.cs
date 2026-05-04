namespace LocalMind.Cache;

public sealed class SemanticCacheOptions
{
    public const string SectionName = "SemanticCache";

    public bool Enabled { get; set; } = true;
    public string CollectionName { get; set; } = "semantic_cache";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public uint EmbeddingDimensions { get; set; } = 768;
    public float SimilarityThreshold { get; set; } = 0.92f;
}