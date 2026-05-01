namespace LocalMind.KnowledgeChatBot;

public sealed class QdrantClientOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 6334;

    public bool Https { get; set; }

    public string? ApiKey { get; set; }

    public TimeSpan GrpcTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
