using LocalMind.Agent;

namespace LocalMind.Application.Agents;

public sealed class AgentInvokeResponse
{
    public required string Agent { get; init; }
    public required string ConversationId { get; init; }
    public required AgentResponse Result { get; init; }
}
