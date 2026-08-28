using LocalMind.Application.Chat;

namespace LocalMind.KnowledgeChatBot;

internal sealed class InProcessChatClient(IChatService chat) : IChatClient
{
    public Task<ChatResponse> SendAsync(string conversationId, string message, CancellationToken ct = default)
        => chat.ExecuteAsync(new ChatRequest
        {
            Message = message,
            ConversationId = conversationId
        }, ct);
}
