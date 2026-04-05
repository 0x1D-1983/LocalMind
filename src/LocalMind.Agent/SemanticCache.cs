namespace LocalMind.Agent;

public class SemanticCache
{
    public Task<AgentResponse> GetAsync(string query, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task SetAsync(string query, AgentResponse? response, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}