namespace LocalMind.Application.Conversations;

public sealed class ConversationDto
{
    public required string Id { get; init; }
    public required IReadOnlyList<ConversationMessageDto> Messages { get; init; }
}

public sealed class ConversationMessageDto
{
    public required string Role { get; init; }
    public string? Content { get; init; }
}
