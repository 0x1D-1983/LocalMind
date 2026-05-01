using CommandLine;
using LocalMind.Ingestion;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using Qdrant.Client;
using Qdrant.Client.Grpc;
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
            services.AddSingleton<ILoggerFactory>(loggerFactory);
            services.AddOllama(configuration);
            services.AddQdrant(configuration);
            services.Configure<KnowledgeIngestOptions>(
                configuration.GetSection(KnowledgeIngestOptions.SectionName));

            using var provider = services.BuildServiceProvider();

            var ollamaOpts = provider.GetRequiredService<IOptions<OllamaApiClientOptions>>().Value;
            var qdrantOpts = provider.GetRequiredService<IOptions<QdrantClientOptions>>().Value;
            var ingestOpts = provider.GetRequiredService<IOptions<KnowledgeIngestOptions>>().Value;

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

            if (string.IsNullOrWhiteSpace(ingestOpts.CollectionName))
            {
                Log.Error("Missing or invalid '{Section}:CollectionName'.", KnowledgeIngestOptions.SectionName);
                return 1;
            }

            if (ingestOpts.EmbeddingDimensions == 0)
            {
                Log.Error("'{Section}:EmbeddingDimensions' must be greater than zero.", KnowledgeIngestOptions.SectionName);
                return 1;
            }

            var ollama = provider.GetRequiredService<OllamaApiClient>();
            using var qdrant = provider.GetRequiredService<QdrantClient>();

            var collectionName = ingestOpts.CollectionName;
            if (!await qdrant.CollectionExistsAsync(collectionName))
            {
                Log.Information("Creating Qdrant collection {CollectionName}", collectionName);
                await qdrant.CreateCollectionAsync(collectionName, new VectorParams
                {
                    Size = ingestOpts.EmbeddingDimensions,
                    Distance = Distance.Cosine
                });
            }

            var ingester = new DocumentIngester(ollama, qdrant, collectionName);
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
