using CommandLine;
using LocalMind.Ingestion;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LocalMind.IngestConsoleApp;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        try
        {
            var exitCode = 1;
            await Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsedAsync(async opts => exitCode = await RunAsync(configuration, opts));
            return exitCode;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static async Task<int> RunAsync(IConfiguration configuration, CommandLineOptions options)
    {
        var path = Path.GetFullPath(options.DocumentPath);
        if (!File.Exists(path))
        {
            Log.Error("File not found: {Path}", path);
            return 1;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));

        try
        {
            var services = new ServiceCollection();
            services.AddOllama(configuration);
            services.AddQdrant(configuration);
            services.AddDocumentIngester(configuration);

            using var provider = services.BuildServiceProvider();

            var ingester = provider.GetRequiredService<DocumentIngester>();
            await ingester.IngestAsync(path);

            Log.Information("Ingested: {Path}", path);
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ingest failed");
            return 1;
        }
    }
}
