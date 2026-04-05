using System.Text.Json.Nodes;

namespace LocalMind.Tools;

// Represents a tool definition sent to the model
public record ToolDefinition(
    string Name,
    string Description,
    JsonObject InputSchema
);