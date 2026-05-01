using System.ComponentModel.DataAnnotations;

namespace LocalMind.Qdrant;

public sealed class QdrantClientOptions
{
    public const string SectionName = "Qdrant";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 6334;

    public bool Https { get; set; }
    public string? ApiKey { get; set; }
    public TimeSpan GrpcTimeout { get; set; } = TimeSpan.FromSeconds(30);
}