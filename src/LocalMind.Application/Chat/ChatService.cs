using LocalMind.Application.Agents;
using LocalMind.Telemetry;

namespace LocalMind.Application.Chat;

public sealed class ChatService(IAgentInvokeService agents) : IChatService
{
    public async Task<ChatResponse> ExecuteAsync(ChatRequest request, CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Application.StartActivity("chat.execute");
        try
        {
            ArgumentNullException.ThrowIfNull(request);

            if (string.IsNullOrWhiteSpace(request.Message))
                throw new ArgumentException("Message is required.", nameof(request));

            activity?.SetTag("conversation.id", request.ConversationId);

            var invoked = await agents.InvokeAsync(
                KnownAgents.Knowledge,
                new AgentInvokeRequest
                {
                    Input = request.Message,
                    ConversationId = request.ConversationId
                },
                ct);

            var result = invoked.Result;
            activity?.SetTag("conversation.id", invoked.ConversationId);
            activity?.SetTag("chat.confidence", result.Confidence);

            return new ChatResponse
            {
                ConversationId = invoked.ConversationId,
                Answer = result.Answer,
                Sources = result.Sources,
                Confidence = result.Confidence,
                ToolsUsed = result.ToolsUsed
            };
        }
        catch (Exception ex)
        {
            activity.RecordError(ex);
            throw;
        }
    }
}
