namespace LocalMind.Agent.Workflow;

public sealed record WorkflowEdge(
    string From,
    string To,
    string Label,
    Func<AgentRunContext, bool>? When = null);

/// <summary>
/// Directed graph of named nodes. <see cref="Next"/> picks the first matching
/// edge; unconditional edges are used only when no predicate matched.
/// </summary>
public sealed class WorkflowGraph(string start, IReadOnlyList<WorkflowEdge> edges)
{
    public string Start { get; } = start;
    public IReadOnlyList<WorkflowEdge> Edges { get; } = edges;

    public WorkflowEdge? Next(string from, AgentRunContext ctx)
    {
        WorkflowEdge? unconditional = null;
        foreach (var edge in Edges)
        {
            if (edge.From != from)
                continue;

            if (edge.When is null)
            {
                unconditional ??= edge;
                continue;
            }

            if (edge.When(ctx))
                return edge;
        }

        return unconditional;
    }
}

public sealed class WorkflowGraphBuilder(string start)
{
    private readonly List<WorkflowEdge> _edges = [];

    public WorkflowGraphBuilder Edge(
        string from,
        string to,
        string? label = null,
        Func<AgentRunContext, bool>? when = null)
    {
        _edges.Add(new WorkflowEdge(from, to, label ?? to, when));
        return this;
    }

    public WorkflowGraph Build() => new(start, _edges);
}
