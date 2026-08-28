namespace LocalMind.Application.Chat;

public sealed class ChatResponse
{
    public required string ConversationId { get; init; }
    public required string Answer { get; init; }
    public string[] Sources { get; init; } = [];
    public float Confidence { get; init; }
    public string[] ToolsUsed { get; init; } = [];
}
