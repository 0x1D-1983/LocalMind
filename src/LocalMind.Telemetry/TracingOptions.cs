using System.ComponentModel.DataAnnotations;

namespace LocalMind.Telemetry;

public sealed class TracingOptions
{
    public const string SectionName = "Tracing";

    public bool Enabled { get; set; } = true;

    /// <summary>OTLP gRPC endpoint (Tempo, Jaeger, or any OTLP collector).</summary>
    [Required]
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";
}
