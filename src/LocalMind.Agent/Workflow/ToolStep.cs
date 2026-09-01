using System.Runtime.CompilerServices;
using LocalMind.Tools;
using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

public sealed class ToolStep(
    ToolExecutor executor,
    AgentResponseProcessor processor,
    ILogger<ToolStep> logger) : IAgentWorkflowNode
{
    public string Name => AgentWorkflowGraph.Tools;

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var message = ctx.LastLlmResponse?.Message
            ?? throw new InvalidOperationException("ToolStep requires a prior LlmStep response.");

        var toolCallNames = message.ToolCalls?
            .Select(tc => tc.Function?.Name ?? "unknown")
            .ToArray() ?? [];

        logger.LogDebug(
            "Iteration {Iteration}: model requested {Count} tool call(s): {Names}",
            ctx.Iteration, toolCallNames.Length, string.Join(", ", toolCallNames));

        var toolResults = await executor.ExecuteAllAsync([message], ct);
        foreach (var result in toolResults)
        {
            if (processor.TryCollectDocumentSourceFiles(
                    result, ctx.DocumentSourceFilesOrdered, ctx.DocumentSourceFilesSeen))
                ctx.DocumentSearchRan = true;

            ctx.History.Add(new Message(ChatRole.Tool, result.Content)
            {
                ToolName = result.ToolName
            });
        }

        // Matches the old for-loop: increment after tools, then decide whether
        // another LLM visit is allowed (Iteration < MaxIterations).
        ctx.Iteration++;
        yield break;
    }
}
