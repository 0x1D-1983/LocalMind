namespace LocalMind.Ollama;

public sealed class OllamaApiClientOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "";
}
