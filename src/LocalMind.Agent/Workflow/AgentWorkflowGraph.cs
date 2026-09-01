namespace LocalMind.Agent.Workflow;

/// <summary>
/// Explicit ReAct graph. The walker in <see cref="AgentWorkflow"/> follows these
/// edges; the loop is not encoded in procedural if/for.
/// <code>
/// START
///   │
///   ▼
/// Rewrite
///   │
///   ▼
/// Cache
///   │
///   ├──── HIT ────► END
///   │
///   ▼ MISS
/// LLM
///   │
///   ├──── no tools ───► Final ───► END
///   │
///   ▼ tools
/// Tools
///   │
///   ├──── continue ───► LLM
///   │
///   └──── exhausted ──► MaxIterationsExceeded
/// </code>
/// </summary>
public static class AgentWorkflowGraph
{
    public const string Rewrite = "Rewrite";
    public const string Cache = "Cache";
    public const string Llm = "LLM";
    public const string Tools = "Tools";
    public const string Final = "Final";
    public const string End = "END";
    public const string MaxIterationsExceeded = "MaxIterationsExceeded";

    public static WorkflowGraph Build() => new WorkflowGraphBuilder(Rewrite)
        .Edge(Rewrite, Cache)
        .Edge(Cache, End, "HIT", ctx => ctx.CacheHit)
        .Edge(Cache, Llm, "MISS", ctx => !ctx.CacheHit)
        .Edge(Llm, Final, "no tools", ctx => !ctx.HasToolCalls)
        .Edge(Llm, Tools, "tools", ctx => ctx.HasToolCalls)
        .Edge(Tools, Llm, "continue", ctx => ctx.Iteration < ctx.MaxIterations)
        .Edge(Tools, MaxIterationsExceeded, "exhausted", ctx => ctx.Iteration >= ctx.MaxIterations)
        .Edge(Final, End)
        .Build();
}
