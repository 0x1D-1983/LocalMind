using System.ComponentModel.DataAnnotations;

namespace LocalMind.Cache;

public sealed class EntityExtractorOptions
{
    public const string SectionName = "EntityExtractor";

    [Required]
    public string Model { get; set; } = "qwen3:8b";
}