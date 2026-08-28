using LocalMind.Application.Agents;

namespace LocalMind.Application.Chat;

public sealed class ChatService(IAgentInvokeService agents) : IChatService
{
    public async Task<ChatResponse> ExecuteAsync(ChatRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Message is required.", nameof(request));

        var invoked = await agents.InvokeAsync(
            KnownAgents.Knowledge,
            new AgentInvokeRequest
            {
                Input = request.Message,
                ConversationId = request.ConversationId
            },
            ct);

        var result = invoked.Result;
        return new ChatResponse
        {
            ConversationId = invoked.ConversationId,
            Answer = result.Answer,
            Sources = result.Sources,
            Confidence = result.Confidence,
            ToolsUsed = result.ToolsUsed
        };
    }
}
