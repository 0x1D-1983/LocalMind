using KnowledgeAgent = LocalMind.Agent.Agent;

namespace LocalMind.Application.Agents;

public sealed class AgentInvokeService(KnowledgeAgent agent) : IAgentInvokeService
{
    public async Task<AgentInvokeResponse> InvokeAsync(
        string agentName,
        AgentInvokeRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(request);

        if (!KnownAgents.Exists(agentName))
            throw new UnknownAgentException(agentName);

        if (string.IsNullOrWhiteSpace(request.Input))
            throw new ArgumentException("Input is required.", nameof(request));

        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId.Trim();

        var result = await agent.RunAsync(conversationId, request.Input.Trim(), ct);

        return new AgentInvokeResponse
        {
            Agent = KnownAgents.Knowledge,
            ConversationId = conversationId,
            Result = result
        };
    }
}
