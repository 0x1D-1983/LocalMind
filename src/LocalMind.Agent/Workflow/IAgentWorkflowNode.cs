namespace LocalMind.Agent.Workflow;

public interface IAgentWorkflowNode
{
    string Name { get; }

    IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        CancellationToken ct = default);
}
