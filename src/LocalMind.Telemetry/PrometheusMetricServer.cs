using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Prometheus;

namespace LocalMind.Telemetry;

public sealed class PrometheusMetricServer(IOptions<PrometheusMetricServerOptions> options) : IHostedService, IDisposable
{
    private MetricServer? _server;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var port = options.Value.Port;
        _server = new MetricServer(port);
        _server.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose() => _server?.Dispose();
}