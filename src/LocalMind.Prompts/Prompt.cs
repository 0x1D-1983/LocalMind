namespace LocalMind.Prompts;

public sealed record Prompt
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Content { get; init; }
}
