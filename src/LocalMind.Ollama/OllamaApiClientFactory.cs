using Microsoft.Extensions.Options;
using OllamaSharp;

namespace LocalMind.Ollama;

public interface IOllamaApiClientFactory
{
    OllamaApiClient CreateClient();
}

public sealed class OllamaApiClientFactory : IOllamaApiClientFactory
{
    private readonly OllamaApiClient _client;
    private readonly OllamaApiClientOptions _ollamaApiClientOptions;

    public OllamaApiClientFactory(IOptionsMonitor<OllamaApiClientOptions> ollamaApiClientOptions)
    {
        _ollamaApiClientOptions = ollamaApiClientOptions.CurrentValue;
        _client = new OllamaApiClient(new Uri(_ollamaApiClientOptions.BaseUrl));
    }

    public OllamaApiClient CreateClient()
    {
        return _client;
    }
}