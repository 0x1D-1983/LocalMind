using System.Diagnostics;
using System.Runtime.CompilerServices;
using LocalMind.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

/// <summary>
/// Walks <see cref="AgentWorkflowGraph"/>. Transitions live on the graph;
/// this type only executes the current node and follows the chosen edge.
/// </summary>
public sealed class AgentWorkflow
{
    private readonly WorkflowGraph _graph;
    private readonly IReadOnlyDictionary<string, IAgentWorkflowNode> _nodes;
    private readonly IOptions<AgentOptions> _agentOptions;
    private readonly ILogger<AgentWorkflow> _logger;

    public AgentWorkflow(
        QueryRewriteStep rewrite,
        CacheStep cache,
        LlmStep llm,
        ToolStep tools,
        FinalResponseStep final,
        IOptions<AgentOptions> agentOptions,
        ILogger<AgentWorkflow> logger)
    {
        _graph = AgentWorkflowGraph.Build();
        _agentOptions = agentOptions;
        _logger = logger;
        _nodes = new Dictionary<string, IAgentWorkflowNode>
        {
            [rewrite.Name] = rewrite,
            [cache.Name] = cache,
            [llm.Name] = llm,
            [tools.Name] = tools,
            [final.Name] = final,
            [AgentWorkflowGraph.End] = new EndNode(),
            [AgentWorkflowGraph.MaxIterationsExceeded] = new MaxIterationsExceededNode(logger),
        };
    }

    public async Task<AgentResponse> RunAsync(
        string sessionId,
        string userQuery,
        IReadOnlyList<Message> persistedTurns,
        CancellationToken ct = default)
    {
        var ctx = CreateContext(sessionId, userQuery, persistedTurns, streaming: false, logCacheHit: true);
        await foreach (var _ in WalkAsync(ctx, ct))
        { }

        return ctx.Result
            ?? throw new AgentException("Workflow reached END without a response.");
    }

    public IAsyncEnumerable<AgentStreamEvent> RunStreamAsync(
        string sessionId,
        string userQuery,
        IReadOnlyList<Message> persistedTurns,
        CancellationToken ct = default)
    {
        var ctx = CreateContext(sessionId, userQuery, persistedTurns, streaming: true, logCacheHit: false);
        return WalkAsync(ctx, ct);
    }

    private async IAsyncEnumerable<AgentStreamEvent> WalkAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var current = _graph.Start;
        ActivitySpan? iteration = null;
        try
        {
            while (true)
            {
                iteration = AdvanceIterationSpan(current, ctx, iteration);

                if (!_nodes.TryGetValue(current, out var node))
                    throw new InvalidOperationException($"Unknown workflow node '{current}'.");

                await foreach (var evt in node.ExecuteAsync(ctx, ct))
                    yield return evt;

                var edge = _graph.Next(current, ctx);
                if (edge is null)
                    yield break;

                _logger.LogDebug("Workflow {From} --{Label}--> {To}", edge.From, edge.Label, edge.To);
                current = edge.To;
            }
        }
        finally
        {
            iteration?.Dispose();
        }
    }

    private AgentRunContext CreateContext(
        string sessionId,
        string userQuery,
        IReadOnlyList<Message> persistedTurns,
        bool streaming,
        bool logCacheHit) => new()
    {
        SessionId = sessionId,
        UserQuery = userQuery,
        UserMessage = new Message(ChatRole.User, userQuery),
        PersistedTurns = persistedTurns,
        History = [],
        MaxIterations = _agentOptions.Value.MaxIterations,
        Streaming = streaming,
        LogCacheHit = logCacheHit
    };

    /// <summary>
    /// One <c>agent.iteration</c> span covers LLM plus any tools that follow,
    /// matching the old for-loop <c>using</c>.
    /// </summary>
    private static ActivitySpan? AdvanceIterationSpan(
        string node,
        AgentRunContext ctx,
        ActivitySpan? current)
    {
        if (node == AgentWorkflowGraph.Llm)
        {
            current?.Dispose();
            var activity = LocalMindActivitySources.Agent.StartActivity("agent.iteration");
            activity?.SetTag("agent.iteration", ctx.Iteration);
            return new ActivitySpan(activity);
        }

        if (node is AgentWorkflowGraph.End or AgentWorkflowGraph.MaxIterationsExceeded)
        {
            current?.Dispose();
            return null;
        }

        return current;
    }

    private readonly record struct ActivitySpan(Activity? Activity) : IDisposable
    {
        public void Dispose() => Activity?.Dispose();
    }

    private sealed class EndNode : IAgentWorkflowNode
    {
        public string Name => AgentWorkflowGraph.End;

        public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
            AgentRunContext ctx,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            if (ctx.Streaming && ctx.Result is { } result)
                yield return new AgentStreamFinal(result);

            await Task.CompletedTask;
        }
    }

    private sealed class MaxIterationsExceededNode(ILogger logger) : IAgentWorkflowNode
    {
        public string Name => AgentWorkflowGraph.MaxIterationsExceeded;

        public IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
            AgentRunContext ctx,
            CancellationToken ct = default)
        {
            ctx.Stopwatch.Stop();
            logger.LogWarning(
                "Agent exceeded max iterations ({Max}) after {ElapsedMs}ms. " +
                "Last history length: {HistoryLength} messages",
                ctx.MaxIterations, ctx.Stopwatch.ElapsedMilliseconds, ctx.History.Count);

            throw new AgentException(
                $"Agent could not produce a final answer within {ctx.MaxIterations} iterations. " +
                $"Last tool calls: {string.Join(", ", ctx.History.LastOrDefault(m => m.Role == ChatRole.Assistant)?.ToolCalls?.Select(t => t.Function?.Name) ?? [])}. " +
                $"Consider increasing MaxIterations or simplifying the query.");
        }
    }
}
