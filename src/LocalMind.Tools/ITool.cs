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
    /// Capability text for the tool manifest: what this tool does, what it returns,
    /// and how to fill its parameters. Do not mention other tools or when to prefer
    /// this tool over others — that belongs in the tool-use policy prompt.
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