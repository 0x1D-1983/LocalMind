using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

public interface IConversationStore
{
    Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetAsync(string sessionId, CancellationToken ct = default);
    Task AppendTurnAsync(string sessionId, Message user, Message assistant, CancellationToken ct = default);
    Task ClearAsync(string sessionId, CancellationToken ct = default);
}