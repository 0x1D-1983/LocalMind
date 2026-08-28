namespace LocalMind.Application.Agents;

public sealed class UnknownAgentException(string agentName)
    : Exception($"Unknown agent '{agentName}'.")
{
    public string AgentName { get; } = agentName;
}
