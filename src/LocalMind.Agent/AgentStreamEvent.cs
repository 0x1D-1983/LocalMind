namespace LocalMind.Agent;

public abstract record AgentStreamEvent;

public sealed record AgentStreamText(string Text) : AgentStreamEvent;

public sealed record AgentStreamFinal(AgentResponse Response) : AgentStreamEvent;

