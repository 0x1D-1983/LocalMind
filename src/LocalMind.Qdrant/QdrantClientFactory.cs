using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;

namespace LocalMind.Qdrant;
public interface IQdrantClientFactory
{
    QdrantClient CreateClient();
}

public sealed class QdrantClientFactory : IQdrantClientFactory
{
    private readonly QdrantClient _client;
    private readonly QdrantClientOptions _qdrantClientOptions;

    public QdrantClientFactory(
        IOptionsMonitor<QdrantClientOptions> qdrantClientOptions,
        ILoggerFactory loggerFactory)
    {
        _qdrantClientOptions = qdrantClientOptions.CurrentValue;
        _client = new QdrantClient(
            _qdrantClientOptions.Host,
            _qdrantClientOptions.Port,
            _qdrantClientOptions.Https,
            _qdrantClientOptions.ApiKey ?? string.Empty,
            _qdrantClientOptions.GrpcTimeout,
            loggerFactory);
    }

    public QdrantClient CreateClient()
    {
        return _client;
    }
}
