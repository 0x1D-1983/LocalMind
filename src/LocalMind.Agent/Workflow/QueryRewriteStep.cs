using System.Runtime.CompilerServices;
using LocalMind.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMind.Agent.Workflow;

public sealed class QueryRewriteStep(
    QueryRewriter rewriter,
    IOptions<SemanticCacheOptions> cacheOptions,
    ILogger<QueryRewriteStep> logger) : IAgentWorkflowNode
{
    public string Name => AgentWorkflowGraph.Rewrite;

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!cacheOptions.Value.Enabled)
        {
            ctx.ResolvedQuery = ctx.UserQuery;
            yield break;
        }

        ctx.ResolvedQuery = await rewriter.RewriteAsync(ctx.UserQuery, ctx.PersistedTurns, ct);
        logger.LogInformation("Resolved query: {ResolvedQuery}", ctx.ResolvedQuery);
    }
}
