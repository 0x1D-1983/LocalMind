using LocalMind.Application.Chat;

namespace LocalMind.KnowledgeChatBot;

internal interface IChatClient
{
    Task<ChatResponse> SendAsync(string conversationId, string message, CancellationToken ct = default);
}
