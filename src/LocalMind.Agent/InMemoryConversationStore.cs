using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

public sealed class InMemoryConversationStore : IConversationStore
{
    // Outer key: sessionId. Inner list: interleaved user/assistant Messages.
    private readonly ConcurrentDictionary<string, List<Message>> _sessions = new();
    private readonly AgentOptions _options;

    public InMemoryConversationStore(IOptions<AgentOptions> options)
        => _options = options.Value;

    public Task<IReadOnlyList<Message>> GetAsync(string sessionId, CancellationToken ct = default)
    {
        if (_sessions.TryGetValue(sessionId, out var history))
            return Task.FromResult<IReadOnlyList<Message>>(history.AsReadOnly());

        return Task.FromResult<IReadOnlyList<Message>>(Array.Empty<Message>());
    }

    public Task AppendTurnAsync(string sessionId, Message user, Message assistant, CancellationToken ct = default)
    {
        var history = _sessions.GetOrAdd(sessionId, _ => []);

        lock (history)
        {
            history.Add(user);
            history.Add(assistant);
            TrimHistory(history); // enforce sliding window
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    private void TrimHistory(List<Message> history)
    {
        // MaxTurns is pairs, so *2 for individual messages
        var maxMessages = _options.MaxConversationTurns * 2;
        if (history.Count > maxMessages)
            history.RemoveRange(0, history.Count - maxMessages);
    }
}