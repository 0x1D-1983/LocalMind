using LocalMind.Agent;
using LocalMind.Telemetry;

namespace LocalMind.Application.Conversations;

public sealed class ConversationService(IConversationStore store) : IConversationService
{
    public async Task<ConversationDto?> GetAsync(string id, CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Application.StartActivity("conversation.get");
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            var sessionId = id.Trim();
            activity?.SetTag("conversation.id", sessionId);

            if (!await store.ExistsAsync(sessionId, ct))
            {
                activity?.SetTag("conversation.found", false);
                return null;
            }

            var messages = await store.GetAsync(sessionId, ct);
            activity?.SetTag("conversation.found", true);
            activity?.SetTag("conversation.message_count", messages.Count);

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
        catch (Exception ex)
        {
            activity.RecordError(ex);
            throw;
        }
    }
}
