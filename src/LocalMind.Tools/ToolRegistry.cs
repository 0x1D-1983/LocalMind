using Microsoft.Extensions.Logging;

namespace LocalMind.Tools;

/// <summary>
/// Holds all registered tools and resolves them by name.
///
/// Registration via DI:
///   services.AddSingleton&lt;ITool, KnowledgeSearchTool&gt;();
///   services.AddSingleton&lt;ITool, DatabaseQueryTool&gt;();
///   services.AddSingleton&lt;IToolRegistry, ToolRegistry&gt;();
///
/// ToolRegistry receives IEnumerable&lt;ITool&gt; automatically — no manual wiring.
/// </summary>
public interface IToolRegistry
{
    bool TryGet(string name, out ITool tool);
    IReadOnlyCollection<ITool> All { get; }
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(IEnumerable<ITool> tools, ILogger<ToolRegistry> logger)
    {
        _logger = logger;
        _tools = new Dictionary<string, ITool>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in tools)
        {
            if (!_tools.TryAdd(tool.Name, tool))
                throw new InvalidOperationException(
                    $"Duplicate tool name '{tool.Name}'. Tool names must be unique.");

            _logger.LogDebug("Registered tool: {ToolName}", tool.Name);
        }

        _logger.LogInformation("Tool registry initialised with {Count} tool(s): {Names}",
            _tools.Count, string.Join(", ", _tools.Keys));
    }

    public bool TryGet(string name, out ITool tool) =>
        _tools.TryGetValue(name, out tool!);

    public IReadOnlyCollection<ITool> All => _tools.Values;
}