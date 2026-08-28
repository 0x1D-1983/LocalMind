namespace LocalMind.Application.Conversations;

public interface IConversationService
{
    Task<ConversationDto?> GetAsync(string id, CancellationToken ct = default);
}
