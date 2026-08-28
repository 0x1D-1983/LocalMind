namespace LocalMind.Prompts;

public sealed class PromptNotFoundException(string name, string? version = null)
    : Exception(version is null
        ? $"Prompt '{name}' was not found."
        : $"Prompt '{name}' version '{version}' was not found.")
{
    public string Name { get; } = name;
    public string? Version { get; } = version;
}
