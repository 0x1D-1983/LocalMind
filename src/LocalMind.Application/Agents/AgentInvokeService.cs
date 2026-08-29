using KnowledgeAgent = LocalMind.Agent.Agent;
using LocalMind.Telemetry;

namespace LocalMind.Application.Agents;

public sealed class AgentInvokeService(KnowledgeAgent agent) : IAgentInvokeService
{
    public async Task<AgentInvokeResponse> InvokeAsync(
        string agentName,
        AgentInvokeRequest request,
        CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Application.StartActivity("agent.invoke");
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
            ArgumentNullException.ThrowIfNull(request);

            activity?.SetTag("agent.name", agentName);

            if (!KnownAgents.Exists(agentName))
                throw new UnknownAgentException(agentName);

            if (string.IsNullOrWhiteSpace(request.Input))
                throw new ArgumentException("Input is required.", nameof(request));

            var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
                ? Guid.NewGuid().ToString("N")
                : request.ConversationId.Trim();

            activity?.SetTag("conversation.id", conversationId);

            var result = await agent.RunAsync(conversationId, request.Input.Trim(), ct);

            return new AgentInvokeResponse
            {
                Agent = KnownAgents.Knowledge,
                ConversationId = conversationId,
                Result = result
            };
        }
        catch (Exception ex)
        {
            activity.RecordError(ex);
            throw;
        }
    }
}
