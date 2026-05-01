namespace LocalMind.Agent;

/// <summary>In-memory placeholder until semantic cache is implemented (Phase 4).</summary>
public class SemanticCache
{
    public Task<AgentResponse?> GetAsync(string query, CancellationToken cancellationToken = default) =>
        Task.FromResult<AgentResponse?>(null);

    public Task SetAsync(string query, AgentResponse? response, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}