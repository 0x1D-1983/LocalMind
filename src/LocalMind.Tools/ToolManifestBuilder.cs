using System.Text.Json.Nodes;
using OllamaSharp.Models.Chat;

namespace LocalMind.Tools;

/// <summary>
/// Converts registered ITool implementations into the tool manifest format
/// that Ollama (and OpenAI-compatible APIs) expect in a chat request.
///
/// Keeps tool definition logic decoupled from the executor and the agent loop.
/// </summary>
public sealed class ToolManifestBuilder
{
    private readonly IToolRegistry _registry;

    public ToolManifestBuilder(IToolRegistry registry) => _registry = registry;

    /// <summary>
    /// Build the full tool manifest to include in each ChatRequest.
    /// The manifest is cheap to build and should be included on every call
    /// so the model always knows what tools are available.
    /// </summary>
    public IEnumerable<Tool> Build() =>
        _registry.All.Select(ToOllamaTool);

    /// <summary>
    /// Build a manifest containing only the named tools.
    /// Useful for sub-agents with a restricted toolset.
    /// </summary>
    public IEnumerable<Tool> Build(params string[] toolNames)
    {
        var names = new HashSet<string>(toolNames, StringComparer.OrdinalIgnoreCase);
        return _registry.All
            .Where(t => names.Contains(t.Name))
            .Select(ToOllamaTool);
    }

    private static Tool ToOllamaTool(ITool tool) => new()
    {
        Type = "function",
        Function = new Function
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = new Parameters
            {
                Type = "object",
                Properties = ExtractProperties(tool.InputSchema),
                Required = ExtractRequired(tool.InputSchema)
            }
        }
    };

    private static Dictionary<string, Property> ExtractProperties(JsonObject schema)
    {
        if (!schema.TryGetPropertyValue("properties", out var propsNode)
            || propsNode is not JsonObject propsObj)
            return [];

        return propsObj.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                var prop = kv.Value as JsonObject ?? new JsonObject();
                return new Property
                {
                    Type = prop["type"]?.GetValue<string>() ?? "string",
                    Description = prop["description"]?.GetValue<string>() ?? string.Empty,
                    Enum = prop["enum"] is JsonArray arr
                        ? arr.Select(n => n?.GetValue<string>()).OfType<string>().ToArray()
                        : null
                };
            });
    }

    private static string[] ExtractRequired(JsonObject schema) =>
        schema.TryGetPropertyValue("required", out var req) && req is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>()).OfType<string>().ToArray()
            : [];
}