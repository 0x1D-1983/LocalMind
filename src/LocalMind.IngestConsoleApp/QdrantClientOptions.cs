namespace LocalMind.IngestConsoleApp;

public sealed class QdrantClientOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";

    /// <summary>gRPC port (default Qdrant: 6334).</summary>
    public int Port { get; set; } = 6334;

    public bool Https { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>gRPC call deadline for Qdrant operations.</summary>
    public TimeSpan GrpcTimeout { get; set; } = TimeSpan.FromMinutes(1);
}
