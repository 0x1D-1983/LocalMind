using System.Diagnostics;
using System.Runtime.CompilerServices;
using LocalMind.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

public sealed class CacheStep(
    SemanticCache<AgentResponse> cache,
    SystemPromptComposer prompts,
    IOptions<SemanticCacheOptions> options,
    ILogger<CacheStep> logger) : IAgentWorkflowNode
{
    public string Name => AgentWorkflowGraph.Cache;

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (options.Value.Enabled)
        {
            var result = await cache.GetAsync(ctx.ResolvedQuery, ct);
            Activity.Current?.SetTag("cache.hit", result.IsHit);

            if (result.IsHit)
            {
                if (ctx.LogCacheHit)
                {
                    logger.LogInformation(
                        "Semantic cache HIT for query in {ElapsedMs}ms, cached at {CachedAt}",
                        ctx.Stopwatch.ElapsedMilliseconds,
                        result.CachedAt);
                }

                ctx.Stopwatch.Stop();
                ctx.CacheHit = true;
                ctx.Result = AgentResponseProcessor.DistinctLists(result.Value);
                yield break;
            }
        }

        ctx.CacheHit = false;
        var systemPrompt = await prompts.ComposeAsync(ct);
        ctx.History.Add(new Message(ChatRole.System, systemPrompt));
        ctx.History.AddRange(ctx.PersistedTurns);
        ctx.History.Add(ctx.UserMessage);
    }

    public async Task SetAsync(string resolvedQuery, AgentResponse response, CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
            return;

        await cache.SetAsync(resolvedQuery, response, ct);
    }
}
