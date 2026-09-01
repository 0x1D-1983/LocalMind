using System.Runtime.CompilerServices;
using LocalMind.Agent.Workflow;
using LocalMind.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMind.Agent;

/// <summary>
/// Public facade for a ReAct run: traces the request, loads persisted turns,
/// then delegates to <see cref="AgentWorkflow"/>.
/// </summary>
public sealed class Agent(
    AgentWorkflow workflow,
    IConversationStore conversationStore,
    IOptions<AgentOptions> agentOptions,
    ILogger<Agent> logger)
{
    private AgentOptions Options => agentOptions.Value;

    public async Task<AgentResponse> RunAsync(
        string sessionId,
        string userQuery,
        CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.run");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("gen_ai.request.model", Options.ModelName);

        logger.LogInformation("Agent query started: SessionId: {SessionId}, Query: {Query}", sessionId, userQuery);

        var persistedTurns = await conversationStore.GetAsync(sessionId, ct);
        return await workflow.RunAsync(sessionId, userQuery, persistedTurns, ct);
    }

    public async IAsyncEnumerable<AgentStreamEvent> RunStreamAsync(
        string sessionId,
        string userQuery,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.run.stream");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("gen_ai.request.model", Options.ModelName);

        logger.LogInformation("Agent streaming query started: SessionId: {SessionId}, Query: {Query}", sessionId, userQuery);

        var persistedTurns = await conversationStore.GetAsync(sessionId, ct);
        await foreach (var evt in workflow.RunStreamAsync(sessionId, userQuery, persistedTurns, ct))
            yield return evt;
    }
}
