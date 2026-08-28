using LocalMind.Application;
using LocalMind.Api;
using LocalMind.Telemetry;
using Serilog;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(dispose: false);

builder.Services
    .AddLocalMindApplication(builder.Configuration)
    .AddPrometheusMetricServer(builder.Configuration);

builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapLocalMindApi();

try
{
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
