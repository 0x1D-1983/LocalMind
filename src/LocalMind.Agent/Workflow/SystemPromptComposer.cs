using LocalMind.Prompts;

namespace LocalMind.Agent.Workflow;

/// <summary>
/// Tool-use policy sits above the agent output contract and above individual
/// tool descriptions, so routing is not duplicated inside each function prompt.
/// </summary>
public sealed class SystemPromptComposer(IPromptProvider prompts)
{
    public async Task<string> ComposeAsync(CancellationToken ct = default)
    {
        var toolPolicy = await prompts.GetAsync(PromptNames.ToolPolicy, ct: ct);
        var agentPrompt = await prompts.GetAsync(PromptNames.KnowledgeAgent, ct: ct);
        return $"{toolPolicy.Content}\n\n{agentPrompt.Content}";
    }
}
