using System.Text.Json.Serialization;

namespace LocalMind.Agent;

/// <summary>
/// The structured output contract returned by every agent run.
///
/// Every field maps 1:1 to the JSON schema in the system prompt (snake_case keys).
/// The model is instructed to populate all fields — Confidence and ToolsUsed
/// give you per-query signal for Grafana dashboards without needing to parse logs.
/// </summary>
public sealed record AgentResponse
{
    [JsonPropertyName("answer")]
    public required string Answer { get; init; }

    [JsonPropertyName("sources")]
    public string[] Sources { get; init; } = [];

    [JsonPropertyName("confidence")]
    public required float Confidence { get; init; }  // 0.0 – 1.0

    /// <summary>Omitted in model JSON deserializes as an empty array.</summary>
    [JsonPropertyName("tools_used")]
    public string[] ToolsUsed { get; init; } = [];

    /// <summary>True when the response was served from the semantic cache.</summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Populated on every live run, null on cache hits.
    /// Strip this before logging responses to LLM context — it's for your eyes only.
    /// </summary>
    public AgentTrace? Trace { get; init; }
}

/// <summary>
/// Execution trace for a single agent run.
/// Emit this as a structured log event so Grafana can track:
///   - Token burn rate per query
///   - Average iterations before a final answer
///   - Tool call frequency by tool name
///   - KV cache effectiveness (PromptEvalDuration across iterations)
/// </summary>
public sealed record AgentTrace
{
    public required int      Iterations           { get; init; }
    public required int      TotalPromptTokens    { get; init; }
    public required int      TotalCompletionTokens{ get; init; }
    public required TimeSpan TotalElapsed         { get; init; }

    /// <summary>
    /// Ordered sequence of tool calls across all iterations, e.g.:
    /// ["search_knowledge_base", "query_database", "calculate"]
    /// Preserves call order within each iteration (parallel calls are interleaved).
    /// </summary>
    public required string[] ToolCallSequence { get; init; }

    /// <summary>
    /// PromptEvalDuration per iteration in milliseconds.
    /// A sharp drop between iteration 0 and 1+ indicates the model's KV cache
    /// is reusing the stable prefix (system prompt + tool manifest).
    /// </summary>
    public required long[] PromptEvalDurationsMs { get; init; }
}

/// <summary>
/// Mutable builder — accumulates stats across loop iterations, then
/// produces an immutable AgentTrace at the end. Keeps Agent.cs clean.
/// </summary>
internal sealed class AgentTraceBuilder
{
    private int      _iterations;
    private int      _promptTokens;
    private int      _completionTokens;
    private readonly List<string> _toolCalls         = [];
    private readonly List<long>   _promptEvalDurations = [];

    public void RecordIteration(
        int promptTokens,
        int completionTokens,
        long promptEvalDurationMs,
        IEnumerable<string> toolCallNames)
    {
        _iterations++;
        _promptTokens     += promptTokens;
        _completionTokens += completionTokens;
        _promptEvalDurations.Add(promptEvalDurationMs);
        _toolCalls.AddRange(toolCallNames);
    }

    public AgentTrace Build(TimeSpan totalElapsed) => new()
    {
        Iterations             = _iterations,
        TotalPromptTokens      = _promptTokens,
        TotalCompletionTokens  = _completionTokens,
        TotalElapsed           = totalElapsed,
        ToolCallSequence       = [.. _toolCalls],
        PromptEvalDurationsMs  = [.. _promptEvalDurations]
    };
}