using System.Text.Json.Nodes;

namespace LocalMind.Tools;

/// <summary>
/// Contract every tool must implement.
/// Name must match exactly what the model emits in its tool_call JSON.
/// </summary>
public interface ITool
{
    /// <summary>Unique tool name — must be snake_case to match LLM convention.</summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description sent to the model in the tool manifest.
    /// This is the model's only signal for when to call this tool — write it carefully.
    /// </summary>
    string Description { get; }

    /// <summary>JSON Schema object describing the tool's input parameters.</summary>
    JsonObject InputSchema { get; }

    /// <summary>
    /// Execute the tool with the given input.
    /// Implementations must never throw — return ToolResult.Fail() instead.
    /// </summary>
    Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default);
}