namespace LocalMind.Agent;

public record AgentResponse
{
    public required string Answer { get; init; }
    public required string[] Sources { get; init; }
    public required float Confidence { get; init; }   // 0.0 - 1.0
    public required string[] ToolsUsed { get; init; }
    public bool FromCache { get; init; }
}