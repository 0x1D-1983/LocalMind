using LocalMind.Agent;
using LocalMind.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace LocalMind.KnowledgeChatBot;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

        var useApi = args.Any(a => a.Equals("--api", StringComparison.OrdinalIgnoreCase));

        var builder = Host.CreateApplicationBuilder(args);

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(dispose: false);

        try
        {
            if (useApi)
            {
                var baseUrl = builder.Configuration["Api:BaseUrl"];
                if (string.IsNullOrWhiteSpace(baseUrl))
                {
                    Console.Error.WriteLine("Api:BaseUrl is required when running with --api.");
                    return 1;
                }

                builder.Services.AddHttpClient<IChatClient, HttpChatClient>(client =>
                {
                    client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
                    client.Timeout = TimeSpan.FromMinutes(10);
                });
            }
            else
            {
                builder.Services.AddLocalMindApplication(builder.Configuration);
                builder.Services.AddSingleton<IChatClient, InProcessChatClient>();
            }

            using var app = builder.Build();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            await app.StartAsync(cts.Token);

            var chat = app.Services.GetRequiredService<IChatClient>();
            var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Chat");

            if (useApi)
                logger.LogInformation("Knowledge chat CLI — talking to {BaseUrl}. Type your question, or 'exit' to quit.", builder.Configuration["Api:BaseUrl"]);
            else
                logger.LogInformation("Knowledge chat (in-process) — type your question, or 'exit' to quit. Use --api to call LocalMind.Api.");

            try
            {
                var sessionId = Guid.NewGuid().ToString("N");
                logger.LogInformation("SessionId: {SessionId}", sessionId);

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
                        var response = await chat.SendAsync(sessionId, trimmed, cts.Token);
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
                    catch (HttpRequestException ex)
                    {
                        logger.LogError(ex, "API request failed");
                        Console.WriteLine($"Error: could not reach LocalMind.Api ({ex.Message}). Start the API, or omit --api to run in-process.");
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
