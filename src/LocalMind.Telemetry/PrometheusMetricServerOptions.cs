namespace LocalMind.Telemetry;

public sealed class PrometheusMetricServerOptions
{
    public const string SectionName = "Metrics";

    public int Port { get; set; } = 9090;
}