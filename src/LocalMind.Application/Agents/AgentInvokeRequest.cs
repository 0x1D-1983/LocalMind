namespace LocalMind.Application.Agents;

public sealed class AgentInvokeRequest
{
    public required string Input { get; init; }
    public string? ConversationId { get; init; }
}
