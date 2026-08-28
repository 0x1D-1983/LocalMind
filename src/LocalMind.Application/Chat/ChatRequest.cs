namespace LocalMind.Application.Chat;

public sealed class ChatRequest
{
    public required string Message { get; init; }
    public string? ConversationId { get; init; }
}
