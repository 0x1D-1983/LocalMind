using System.Diagnostics;
using System.Text.Json;
using LocalMind.Cache;
using LocalMind.Telemetry;
using LocalMind.Tools;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

/// <summary>
/// The ReAct (Reasoning + Acting) agent loop.
///
/// Each call to RunAsync is completely stateless — all context is built fresh
/// and injected into the prompt. This is the fundamental constraint of LLM APIs:
/// the model holds no memory between calls; every call must carry the full world.
///
/// The loop:
///   [cache check] → [build history] → loop {
///     [call model] → [tool calls?] → YES → [execute tools] → [append results] → repeat
///                                  → NO  → [parse JSON] → [cache] → return
///   }
/// </summary>
public sealed class Agent(
        OllamaApiClient ollama,
        ToolExecutor executor,
        ToolManifestBuilder manifest,
        IConversationStore conversationStore,
        SemanticCache<AgentResponse> semanticCache,
        AgentOptions agentOptions,
        SemanticCacheOptions semanticCacheOptions,
        ILogger<Agent> logger,
        IStructuredOutputParser structuredOutputParser,
        QueryRewriter queryRewriter,
        LlmCallMetrics llmCallMetrics)
{
    public async Task<AgentResponse> RunAsync(
        string sessionId,
        string userQuery,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        string resolvedQuery = userQuery;

        logger.LogInformation("Agent query started: SessionId: {SessionId}, Query: {Query}", sessionId, userQuery);

        // Load persisted turns, slot them between system prompt and current user message
        var persistedTurns = await conversationStore.GetAsync(sessionId, ct);

        // ── Phase 1: Semantic cache ───────────────────────────────────────────
        // Check before touching the model — a cache hit costs only one embedding
        // call (~5ms) vs a full agent run (~500ms–5s depending on iterations).
        if (semanticCacheOptions.Enabled)
        {
            // Rewrite first — resolve pronouns against conversation history
            resolvedQuery = await queryRewriter.RewriteAsync(userQuery, persistedTurns, ct);
            logger.LogInformation("Resolved query: {ResolvedQuery}", resolvedQuery);
            
            // Cache lookup uses the resolved, self-contained query
            var cacheResult = await semanticCache.GetAsync(resolvedQuery, ct);
            if (cacheResult.IsHit)
            {
                sw.Stop();
                logger.LogInformation("Semantic cache HIT for query in {ElapsedMs}ms, cached at {CachedAt}", sw.ElapsedMilliseconds, cacheResult.CachedAt);
                return cacheResult.Value;
            }
        }

        // ── Phase 2: Build initial conversation ───────────────────────────────
        // Message ordering for maximum KV cache reuse:
        //   [0] System   — stable: instructions + JSON schema (never changes)
        //   [1] User     — volatile: the query
        //
        // Tool definitions go in the ChatRequest.Tools field, not inline in the
        // system prompt, so Ollama handles their serialisation format correctly.
        
        var userMessage = new Message(ChatRole.User, userQuery);

        var history = new List<Message>(persistedTurns.Count + 2)
        {
            new(ChatRole.System, Prompts.SystemPrompt)
        };

        history.AddRange(persistedTurns);   // previous clean turns
        history.Add(userMessage);            // this turn's user message

        var trace = new AgentTraceBuilder();
        var kbSourceFilesOrdered = new List<string>();
        var kbSourceFilesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Phase 3: ReAct loop ───────────────────────────────────────────────
        for (int iteration = 0; iteration < agentOptions.MaxIterations; iteration++)
        {
            logger.LogDebug("ReAct iteration {Iteration}", iteration);

            var llmResponse = await CallModelAsync(history, ct);
            var llmDoneResponse = (ChatDoneResponseStream)llmResponse;

            // Accumulate token usage and KV cache timing for the trace.
            // PromptEvalDuration from Ollama is in nanoseconds; we store milliseconds in the trace.
            // It is the KV cache signal: fast after the first iteration when the stable prefix is cached.
            var toolCallNames = llmResponse.Message.ToolCalls?
                .Select(tc => tc.Function?.Name ?? "unknown")
                .ToArray() ?? [];

            trace.RecordIteration(
                promptTokens:        llmDoneResponse.PromptEvalCount,
                completionTokens:    llmDoneResponse.EvalCount,
                promptEvalDurationMs: llmDoneResponse.PromptEvalDuration / 1_000_000L,
                toolCallNames:       toolCallNames);

            // IMPORTANT: Always append the assistant message to history before
            // anything else. The model's tool call decisions must remain in context
            // so that when we add tool results, the conversation is coherent.
            history.Add(llmResponse.Message);

            var toolCalls = llmResponse.Message.ToolCalls?.ToList() ?? [];

            // ── No tool calls → final answer ─────────────────────────────────
            if (toolCalls.Count == 0)
            {
                sw.Stop();

                var traceSnapshot = trace.Build(sw.Elapsed);
                var response = await structuredOutputParser.ParseFinalResponseAsync(
                    raw:    llmResponse.Message.Content ?? string.Empty,
                    trace:  traceSnapshot,
                    ct:     ct);

                response = GroundKnowledgeSources(response, kbSourceFilesOrdered, traceSnapshot);

                // logger.LogInformation(
                //     "Agent completed in {Iterations} iteration(s), {TotalMs}ms, " +
                //     "{PromptTokens} prompt tokens, {CompletionTokens} completion tokens",
                //     iteration + 1, sw.ElapsedMilliseconds,
                //     response.Trace!.TotalPromptTokens,
                //     response.Trace!.TotalCompletionTokens);

                // Emit structured log events for every LLM interaction
                logger.LogInformation("LLM call completed {@LlmTrace}", new {
                    Model = agentOptions.ModelName,
                    PromptTokens = response.Trace!.TotalPromptTokens,
                    CompletionTokens = response.Trace!.TotalCompletionTokens,
                    DurationMs = sw.ElapsedMilliseconds,
                    ToolCallsRequested = toolCalls.Count,
                    CacheHit = false,
                    Iteration = iteration + 1,
                });

                llmCallMetrics.RecordCompletedCall(
                    model: agentOptions.ModelName,
                    promptTokens: response.Trace!.TotalPromptTokens,
                    completionTokens: response.Trace!.TotalCompletionTokens,
                    durationMs: sw.ElapsedMilliseconds,
                    toolCallsRequested: toolCalls.Count,
                    cacheHit: false,
                    iteration: iteration + 1);

                // Store in semantic cache for future queries
                if (semanticCacheOptions.Enabled)
                    await semanticCache.SetAsync(resolvedQuery, response, ct);

                // Persist only the clean user/assistant pair
                await conversationStore.AppendTurnAsync(
                    sessionId,
                    userMessage,
                    new Message(ChatRole.Assistant, llmResponse.Message.Content ?? string.Empty),
                    ct);

                return response;
            }

            // ── Tool calls → execute and feed results back ────────────────────
            logger.LogDebug(
                "Iteration {Iteration}: model requested {Count} tool call(s): {Names}",
                iteration, toolCalls.Count, string.Join(", ", toolCallNames));

            // Parallel execution — ToolExecutor.ExecuteAllAsync uses Task.WhenAll.
            // Wall-clock time ≈ slowest single tool, not sum of all tools.
            var toolResults = await executor.ExecuteAllAsync([llmResponse.Message], ct);

            // Append each result as a ChatRole.Tool message.
            // The Name field tells the model which tool produced this result,
            // so it can correlate results with its earlier tool call decisions.
            foreach (var result in toolResults)
            {
                AppendKnowledgeSearchSourceFiles(result, kbSourceFilesOrdered, kbSourceFilesSeen);
                history.Add(new Message(ChatRole.Tool, result.Content)
                {
                    ToolName = result.ToolName
                });
            }

            // Loop continues — model will see tool results and either call more
            // tools or produce a final answer.
        }

        // ── Iteration limit exceeded ──────────────────────────────────────────
        sw.Stop();
        logger.LogWarning(
            "Agent exceeded max iterations ({Max}) after {ElapsedMs}ms. " +
            "Last history length: {HistoryLength} messages",
            agentOptions.MaxIterations, sw.ElapsedMilliseconds, history.Count);

        throw new AgentException(
            $"Agent could not produce a final answer within {agentOptions.MaxIterations} iterations. " +
            $"Last tool calls: {string.Join(", ", history.LastOrDefault(m => m.Role == ChatRole.Assistant)?.ToolCalls?.Select(t => t.Function?.Name) ?? [])}. " +
            $"Consider increasing MaxIterations or simplifying the query.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: model call
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls Ollama and collects the complete response.
    ///
    /// OllamaSharp 4.x streams by default via IAsyncEnumerable.
    /// We set Stream = false and take the final (and only) chunk, which contains
    /// the complete message, token counts, and timing stats.
    ///
    /// Isolating this in one method means if OllamaSharp's API changes,
    /// there's exactly one place to update.
    /// </summary>
    private async Task<ChatResponseStream> CallModelAsync(
        List<Message> history,
        CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Model    = agentOptions.ModelName,
            Messages = history,
            Tools    = manifest.Build(),
            Stream   = false,
            // Qwen3 has a "thinking" mode that emits <think>...</think> tokens.
            // These count toward your token budget but are useful for complex
            // multi-hop reasoning. Disable by adding: Options = new() { ... }
            // or by appending "/no_think" to the model name in Ollama.
        };

        // LastOrDefaultAsync because with Stream=false, Ollama returns a single
        // chunk containing the complete response. If Stream were true, we'd need
        // to aggregate content across chunks (not done here).
        var response = await ollama.ChatAsync(request, ct)
            .LastOrDefaultAsync(ct);

        if (response is null)
            throw new AgentException(
                $"Ollama returned no response for model '{agentOptions.ModelName}'. " +
                "Is the model pulled? Run: ollama pull " + agentOptions.ModelName);

        return response;
    }

    

    /// <summary>
    /// Models often hallucinate plausible filenames in <c>sources</c>. When <c>search_knowledge_base</c> ran,
    /// ground markdown-like entries using filenames from the tool JSON. Non-markdown source strings (e.g. DB tables)
    /// from the model are kept when they do not duplicate grounded files.
    /// </summary>
    private static AgentResponse GroundKnowledgeSources(
        AgentResponse response,
        List<string> kbSourceFilesOrdered,
        AgentTrace trace)
    {
        var kbRan = trace.ToolCallSequence.Any(static n => n == "search_knowledge_base");
        if (!kbRan)
            return response;

        static bool LooksLikeMarkdownFile(string s) =>
            s.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        var nonMdFromModel = response.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s) && !LooksLikeMarkdownFile(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (kbSourceFilesOrdered.Count > 0)
        {
            var merged = new List<string>(kbSourceFilesOrdered);
            foreach (var s in nonMdFromModel)
            {
                if (!merged.Exists(t => t.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    merged.Add(s);
            }

            return response with { Sources = [.. merged] };
        }

        // KB ran but no filenames parsed — drop invented .md / .markdown only.
        return response with { Sources = [.. nonMdFromModel] };
    }

    private static void AppendKnowledgeSearchSourceFiles(
        ToolResult result,
        List<string> ordered,
        HashSet<string> seen)
    {
        if (result.ToolName != "search_knowledge_base" || !result.IsSuccess)
            return;

        try
        {
            using var doc = JsonDocument.Parse(result.Content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var file = "";
                if (el.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                    file = fn.GetString() ?? "";
                if (string.IsNullOrEmpty(file)
                    && el.TryGetProperty("file", out var f)
                    && f.ValueKind == JsonValueKind.String)
                    file = f.GetString() ?? "";
                if (string.IsNullOrEmpty(file)
                    && el.TryGetProperty("source", out var s)
                    && s.ValueKind == JsonValueKind.String)
                {
                    var path = s.GetString();
                    if (!string.IsNullOrEmpty(path))
                        file = Path.GetFileName(path);
                }

                if (string.IsNullOrEmpty(file) || !seen.Add(file))
                    continue;
                ordered.Add(file);
            }
        }
        catch (JsonException)
        {
            // Malformed tool JSON — leave sources unchanged for this result.
        }
    }
}