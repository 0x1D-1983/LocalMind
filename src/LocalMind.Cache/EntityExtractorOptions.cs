namespace LocalMind.Cache;

public sealed class EntityExtractorOptions
{
    public const string SectionName = "EntityExtractor";

    public string Model { get; set; } = "qwen3:8b";
}