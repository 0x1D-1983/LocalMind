namespace LocalMind.Prompts;

public interface IPromptProvider
{
    Task<Prompt> GetAsync(
        string name,
        string? version = null,
        CancellationToken ct = default);
}
