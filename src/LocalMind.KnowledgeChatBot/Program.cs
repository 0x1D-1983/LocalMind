using LocalMind.Agent;
using LocalMind.Ollama;
using LocalMind.Qdrant;
using LocalMind.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

// Class and namespace are both named Agent; unqualified `Agent` resolves to the namespace here.
using AgentApp = LocalMind.Agent.Agent;

namespace LocalMind.KnowledgeChatBot;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var builder = Host.CreateApplicationBuilder(args);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);

        try
        {
            builder.Services
                .AddOllama(builder.Configuration)
                .AddQdrant(builder.Configuration)
                .AddToolInfrastructure(builder.Configuration)
                .AddTool<KnowledgeSearchTool>();

            builder.Services.AddSingleton<SemanticCache>();
            builder.Services.AddAgent(builder.Configuration);

            using var app = builder.Build();

            var agent = app.Services.GetRequiredService<AgentApp>();
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Chat");

            logger.LogInformation("Knowledge chat — type your question, or 'exit' to quit.");

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                while (!cts.IsCancellationRequested)
                {
                    Console.Write("> ");
                    var line = await Console.In.ReadLineAsync(cts.Token);
                    if (line is null)
                        break;

                    var trimmed = line.Trim();
                    if (trimmed.Length == 0)
                        continue;
                    if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)
                        || trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
                        break;

                    try
                    {
                        var response = await agent.RunAsync(trimmed, cts.Token);
                        Console.WriteLine();
                        Console.WriteLine(response.Answer);
                        if (response.Sources.Length > 0)
                            Console.WriteLine($"Sources: {string.Join(", ", response.Sources)}");
                        Console.WriteLine();
                    }
                    catch (AgentException ex)
                    {
                        logger.LogError(ex, "Agent failed");
                        Console.WriteLine($"Error: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
            }

            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
