using LocalMind.Agent;

namespace LocalMind.Application.Conversations;

public sealed class ConversationService(IConversationStore store) : IConversationService
{
    public async Task<ConversationDto?> GetAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var sessionId = id.Trim();
        if (!await store.ExistsAsync(sessionId, ct))
            return null;

        var messages = await store.GetAsync(sessionId, ct);
        return new ConversationDto
        {
            Id = sessionId,
            Messages = messages
                .Select(m => new ConversationMessageDto
                {
                    Role = m.Role.ToString() ?? "unknown",
                    Content = m.Content
                })
                .ToList()
        };
    }
}
