namespace LocalMind.Application.Agents;

public interface IAgentInvokeService
{
    Task<AgentInvokeResponse> InvokeAsync(
        string agent,
        AgentInvokeRequest request,
        CancellationToken ct = default);
}
