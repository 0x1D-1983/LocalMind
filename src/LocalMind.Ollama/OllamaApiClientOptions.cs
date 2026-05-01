using System.ComponentModel.DataAnnotations;

namespace LocalMind.Ollama;

public sealed class OllamaApiClientOptions
{
    public const string SectionName = "Ollama";

    [Required]
    public string BaseUrl { get; set; } = "";
}
