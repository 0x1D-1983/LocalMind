using System.Diagnostics;

namespace LocalMind.Telemetry;

public static class LocalMindActivitySources
{
    public static readonly ActivitySource Application = new("LocalMind.Application");
    public static readonly ActivitySource Agent = new("LocalMind.Agent");
    public static readonly ActivitySource Tools = new("LocalMind.Tools");
    public static readonly ActivitySource Cache = new("LocalMind.Cache");
    public static readonly ActivitySource Ingestion = new("LocalMind.Ingestion");

    public static readonly string[] Names =
    [
        Application.Name,
        Agent.Name,
        Tools.Name,
        Cache.Name,
        Ingestion.Name
    ];
}

public static class ActivityExtensions
{
    public static void RecordError(this Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }
}
