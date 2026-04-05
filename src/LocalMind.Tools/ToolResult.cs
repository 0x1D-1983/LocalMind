namespace LocalMind.Tools;

/// <summary>
/// The result of a single tool execution.
/// Both success and failure flow back into the LLM conversation as tool_result messages.
/// Failures use a structured error payload so the model can self-correct or explain.
/// </summary>
public sealed record ToolResult
{
    public required string ToolName { get; init; }

    /// <summary>
    /// The content string returned to the model as a tool_result message.
    /// On success: the actual result (JSON, plain text, etc).
    /// On failure: a structured error message the model can reason about.
    /// </summary>
    public required string Content { get; init; }

    public bool IsSuccess { get; init; }

    /// <summary>Wall-clock duration of the tool call — useful for Grafana.</summary>
    public TimeSpan Elapsed { get; init; }

    public static ToolResult Ok(string toolName, string content, TimeSpan elapsed) =>
        new() { ToolName = toolName, Content = content, IsSuccess = true, Elapsed = elapsed };

    /// <summary>
    /// Structured failure. The error message is returned to the model verbatim —
    /// phrase it so the model can understand what went wrong and potentially retry
    /// with corrected input.
    /// </summary>
    public static ToolResult Fail(string toolName, string error, TimeSpan elapsed) =>
        new()
        {
            ToolName = toolName,
            Content = $$$"""{"error": "{{{error}}}"}""",
            IsSuccess = false,
            Elapsed = elapsed
        };
}