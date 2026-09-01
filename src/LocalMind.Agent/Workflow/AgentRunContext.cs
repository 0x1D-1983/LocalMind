using System.Diagnostics;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

/// <summary>
/// Mutable state for a single agent run. Steps read and write this bag;
/// it is not shared across sessions or concurrent runs.
/// </summary>
public sealed class AgentRunContext
{
    public required string SessionId { get; init; }
    public required string UserQuery { get; init; }
    public required Message UserMessage { get; init; }
    public required IReadOnlyList<Message> PersistedTurns { get; init; }
    public required List<Message> History { get; init; }
    public required int MaxIterations { get; init; }
    public required bool Streaming { get; init; }
    public required bool LogCacheHit { get; init; }

    public string ResolvedQuery { get; set; } = "";
    public int Iteration { get; set; }
    public ChatResponseStream? LastLlmResponse { get; set; }

    public bool CacheHit { get; set; }
    public AgentResponse? Result { get; set; }

    public bool HasToolCalls => LastLlmResponse?.Message.ToolCalls?.Any() == true;

    internal AgentTraceBuilder Trace { get; } = new();
    public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();

    public List<string> DocumentSourceFilesOrdered { get; } = [];
    public HashSet<string> DocumentSourceFilesSeen { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DocumentSearchRan { get; set; }
}
