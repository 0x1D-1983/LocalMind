namespace LocalMind.IngestConsoleApp;

public sealed class OllamaApiClientOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL for the Ollama HTTP API (e.g. <c>http://localhost:11434</c>).</summary>
    public string BaseUrl { get; set; } = "";
}
