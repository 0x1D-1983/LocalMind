namespace LocalMind.Agent;

// ─────────────────────────────────────────────────────────────────────────────
// Exceptions
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Thrown when the agent cannot complete — max iterations exceeded, Ollama
/// returned no response, or the query is fundamentally unanswerable.
/// Distinct from tool failures (which are returned as ToolResult.Fail and
/// fed back to the model) — this exception escapes the agent entirely.
/// </summary>
public sealed class AgentException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Thrown internally during structured output validation.
/// Caught by ParseFinalResponseAsync's retry loop — never escapes the agent.
/// </summary>
internal sealed class AgentResponseValidationException(string message)
    : Exception(message);