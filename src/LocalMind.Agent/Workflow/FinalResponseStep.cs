using System.Runtime.CompilerServices;
using System.Text;
using LocalMind.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent.Workflow;

public sealed class FinalResponseStep(
    IStructuredOutputParser parser,
    AgentResponseProcessor processor,
    CacheStep cache,
    IConversationStore conversationStore,
    IOptions<AgentOptions> agentOptions,
    LlmCallMetrics llmCallMetrics,
    LlmStep llm,
    ILogger<FinalResponseStep> logger) : IAgentWorkflowNode
{
    public string Name => AgentWorkflowGraph.Final;

    private AgentOptions Options => agentOptions.Value;

    public async IAsyncEnumerable<AgentStreamEvent> ExecuteAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (ctx.Streaming)
        {
            await foreach (var evt in CompleteStreamingAsync(ctx, ct))
                yield return evt;
            yield break;
        }

        ctx.Result = await CompleteAsync(ctx, ct);
    }

    public async Task<AgentResponse> CompleteAsync(AgentRunContext ctx, CancellationToken ct = default)
    {
        ctx.Stopwatch.Stop();
        var raw = ctx.LastLlmResponse?.Message.Content ?? string.Empty;
        var response = await ParseAndGroundAsync(ctx, raw, ct);
        await PersistAsync(ctx, raw, response, ct);
        return response;
    }

    public async IAsyncEnumerable<AgentStreamEvent> CompleteStreamingAsync(
        AgentRunContext ctx,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ctx.History.RemoveAt(ctx.History.Count - 1);

        var streamedText = new StringBuilder();
        ChatDoneResponseStream? streamedDone = null;
        var answerExtractor = new JsonAnswerStreamExtractor();

        await foreach (var chunk in llm.StreamAsync(ctx, ct))
        {
            if (!string.IsNullOrEmpty(chunk.Message.Content))
            {
                streamedText.Append(chunk.Message.Content);
                foreach (var piece in answerExtractor.Push(chunk.Message.Content))
                    yield return new AgentStreamText(piece);
            }

            if (chunk is ChatDoneResponseStream done)
                streamedDone = done;
        }

        if (streamedDone is null)
            throw new AgentException(
                $"Ollama returned no response for model '{Options.ModelName}'. " +
                "Is the model pulled? Run: ollama pull " + Options.ModelName);

        streamedDone.Message.Content = streamedText.ToString();
        ctx.History.Add(streamedDone.Message);
        ctx.LastLlmResponse = streamedDone;

        ctx.Stopwatch.Stop();
        var raw = streamedDone.Message.Content ?? string.Empty;
        ctx.Result = await ParseAndGroundAsync(ctx, raw, ct);
        await PersistAsync(ctx, raw, ctx.Result, ct);
    }

    private async Task<AgentResponse> ParseAndGroundAsync(
        AgentRunContext ctx,
        string raw,
        CancellationToken ct)
    {
        var traceSnapshot = ctx.Trace.Build(ctx.Stopwatch.Elapsed);
        var response = await parser.ParseFinalResponseAsync(raw, traceSnapshot, ct);
        response = processor.GroundSources(
            response, ctx.DocumentSourceFilesOrdered, ctx.DocumentSearchRan);

        logger.LogInformation("LLM call completed {@LlmTrace}", new {
            Model = Options.ModelName,
            PromptTokens = response.Trace!.TotalPromptTokens,
            CompletionTokens = response.Trace!.TotalCompletionTokens,
            DurationMs = ctx.Stopwatch.ElapsedMilliseconds,
            ToolCallsRequested = traceSnapshot.ToolCallSequence.Length,
            CacheHit = false,
            Iteration = ctx.Iteration + 1,
        });

        llmCallMetrics.RecordCompletedCall(
            model: Options.ModelName,
            promptTokens: response.Trace!.TotalPromptTokens,
            completionTokens: response.Trace!.TotalCompletionTokens,
            durationMs: ctx.Stopwatch.ElapsedMilliseconds,
            toolCallsRequested: traceSnapshot.ToolCallSequence.Length,
            cacheHit: false,
            iteration: ctx.Iteration + 1);

        return response;
    }

    private async Task PersistAsync(
        AgentRunContext ctx,
        string assistantContent,
        AgentResponse response,
        CancellationToken ct)
    {
        await cache.SetAsync(ctx.ResolvedQuery, response, ct);
        await conversationStore.AppendTurnAsync(
            ctx.SessionId,
            ctx.UserMessage,
            new Message(ChatRole.Assistant, assistantContent),
            ct);
    }
}
