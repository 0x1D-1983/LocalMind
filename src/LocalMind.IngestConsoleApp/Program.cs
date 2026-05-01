using CommandLine;
using LocalMind.Ingestion;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Serilog;

namespace LocalMind.IngestConsoleApp;

internal static class Program
{
    private const string CollectionName = "knowledge";
    /// <summary>Vector size for <c>nomic-embed-text</c> (Ollama default).</summary>
    private const uint EmbeddingDimensions = 768;

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
            services.AddSingleton<ILoggerFactory>(loggerFactory);
            services.AddOllama(configuration);
            services.AddQdrant(configuration);

            using var provider = services.BuildServiceProvider();

            var ollamaOpts = provider.GetRequiredService<IOptions<OllamaApiClientOptions>>().Value;
            var qdrantOpts = provider.GetRequiredService<IOptions<QdrantClientOptions>>().Value;

            if (string.IsNullOrWhiteSpace(ollamaOpts.BaseUrl))
            {
                Log.Error("Missing or invalid '{Section}' configuration.", OllamaApiClientOptions.SectionName);
                return 1;
            }

            if (string.IsNullOrWhiteSpace(qdrantOpts.Host))
            {
                Log.Error("Missing or invalid '{Section}' configuration.", QdrantClientOptions.SectionName);
                return 1;
            }

            var ollama = provider.GetRequiredService<IOllamaApiClientFactory>().CreateClient();
            using var qdrant = provider.GetRequiredService<IQdrantClientFactory>().CreateClient();

            if (!await qdrant.CollectionExistsAsync(CollectionName))
            {
                Log.Information("Creating Qdrant collection {CollectionName}", CollectionName);
                await qdrant.CreateCollectionAsync(CollectionName, new VectorParams
                {
                    Size = EmbeddingDimensions,
                    Distance = Distance.Cosine
                });
            }

            var ingester = new DocumentIngester(ollama, qdrant);
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

    private sealed class CommandLineOptions
    {
        [Option('d', "document", Required = true, HelpText = "Path to the document file to ingest.")]
        public string DocumentPath { get; set; } = "";
    }
}
