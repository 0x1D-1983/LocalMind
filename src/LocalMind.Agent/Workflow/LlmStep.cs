using System.Runtime.CompilerServices;
using LocalMind.Telemetry;
using LocalMind.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

public sealed class LlmStep(
    OllamaApiClient ollama,
    ToolManifestBuilder manifest,
    IOptions<AgentOptions> agentOptions,
    ILogger<LlmStep> logger) : IAgentWorkflowNode
{
    public string Name => AgentWorkflowGraph.Llm;

    private AgentOptions Options => agentOptions.Value;

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        logger.LogDebug("ReAct iteration {Iteration}", ctx.Iteration);
        await CompleteAsync(ctx, ct);
        yield break;
    }

    public async Task CompleteAsync(AgentRunContext ctx, CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.llm.chat");
        activity?.SetTag("gen_ai.request.model", Options.ModelName);

        var request = new ChatRequest
        {
            Model = Options.ModelName,
            Messages = ctx.History,
            Tools = manifest.Build(),
            Stream = false
        };

        // LastOrDefaultAsync because with Stream=false, Ollama returns a single
        // chunk containing the complete response.
        var response = await ollama.ChatAsync(request, ct)
            .LastOrDefaultAsync(ct);

        if (response is null)
            throw new AgentException(
                $"Ollama returned no response for model '{Options.ModelName}'. " +
                "Is the model pulled? Run: ollama pull " + Options.ModelName);

        Record(ctx, response);
    }

    public IAsyncEnumerable<ChatResponseStream> StreamAsync(
        AgentRunContext ctx,
        CancellationToken ct = default)
    {
        var request = new ChatRequest
        {
            Model = Options.ModelName,
            Messages = ctx.History,
            Tools = manifest.Build(),
            Stream = true
        };

        return ollama.ChatAsync(request, ct)
            .Where(static chunk => chunk is not null)!
            .Select(static chunk => chunk!);
    }

    public void Record(AgentRunContext ctx, ChatResponseStream response)
    {
        var done = (ChatDoneResponseStream)response;
        var toolCallNames = response.Message.ToolCalls?
            .Select(tc => tc.Function?.Name ?? "unknown")
            .ToArray() ?? [];

        ctx.Trace.RecordIteration(
            promptTokens: done.PromptEvalCount,
            completionTokens: done.EvalCount,
            promptEvalDurationMs: done.PromptEvalDuration / 1_000_000L,
            toolCallNames: toolCallNames);

        ctx.History.Add(response.Message);
        ctx.LastLlmResponse = response;
        logger.LogInformation("LLM Thinking: {Thinking}", response.Message.Thinking);
    }
}
