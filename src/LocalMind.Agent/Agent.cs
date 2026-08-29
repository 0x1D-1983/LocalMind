using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using LocalMind.Cache;
using LocalMind.Prompts;
using LocalMind.Telemetry;
using LocalMind.Tools;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

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
        LlmCallMetrics llmCallMetrics,
        IPromptProvider prompts)
{
    public async Task<AgentResponse> RunAsync(
        string sessionId,
        string userQuery,
        CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.run");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("gen_ai.request.model", agentOptions.ModelName);

        var sw = Stopwatch.StartNew();
        string resolvedQuery = userQuery;

        logger.LogInformation("Agent query started: SessionId: {SessionId}, Query: {Query}", sessionId, userQuery);

        // Load persisted turns, slot them between system prompt and current user message
        var persistedTurns = await conversationStore.GetAsync(sessionId, ct);

        // Semantic cache
        if (semanticCacheOptions.Enabled)
        {
            // Rewrite first — resolve pronouns against conversation history
            resolvedQuery = await queryRewriter.RewriteAsync(userQuery, persistedTurns, ct);
            logger.LogInformation("Resolved query: {ResolvedQuery}", resolvedQuery);
            
            // Cache lookup uses the resolved, self-contained query
            var cacheResult = await semanticCache.GetAsync(resolvedQuery, ct);
            if (cacheResult.IsHit)
            {
                activity?.SetTag("cache.hit", true);
                sw.Stop();
                logger.LogInformation("Semantic cache HIT for query in {ElapsedMs}ms, cached at {CachedAt}", sw.ElapsedMilliseconds, cacheResult.CachedAt);
                return cacheResult.Value;
            }

            activity?.SetTag("cache.hit", false);
        }

        var systemPrompt = await ComposeSystemPromptAsync(ct);

        // Build initial conversation 
        var userMessage = new Message(ChatRole.User, userQuery);

        var history = new List<Message>(persistedTurns.Count + 2)
        {
            new(ChatRole.System, systemPrompt)
        };

        history.AddRange(persistedTurns);   // previous clean turns
        history.Add(userMessage);            // this turn's user message

        var trace = new AgentTraceBuilder();
        var documentSourceFilesOrdered = new List<string>();
        var documentSourceFilesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentSearchRan = false;

        // ReAct loop
        for (int iteration = 0; iteration < agentOptions.MaxIterations; iteration++)
        {
            using var iterationActivity = LocalMindActivitySources.Agent.StartActivity("agent.iteration");
            iterationActivity?.SetTag("agent.iteration", iteration);

            logger.LogDebug("ReAct iteration {Iteration}", iteration);

            var llmResponse = await CallModelDoneAsync(history, ct);
            var llmDoneResponse = (ChatDoneResponseStream)llmResponse;

            var toolCallNames = llmResponse.Message.ToolCalls?
                .Select(tc => tc.Function?.Name ?? "unknown")
                .ToArray() ?? [];

            trace.RecordIteration(
                promptTokens:        llmDoneResponse.PromptEvalCount,
                completionTokens:    llmDoneResponse.EvalCount,
                promptEvalDurationMs: llmDoneResponse.PromptEvalDuration / 1_000_000L,
                toolCallNames:       toolCallNames);

            history.Add(llmResponse.Message);

            logger.LogInformation("LLM Thinking: {Thinking}", llmResponse.Message.Thinking);

            var toolCalls = llmResponse.Message.ToolCalls?.ToList() ?? [];

            // No tool calls, final answer 
            if (toolCalls.Count == 0)
            {
                sw.Stop();

                var traceSnapshot = trace.Build(sw.Elapsed);
                var response = await structuredOutputParser.ParseFinalResponseAsync(
                    raw:    llmResponse.Message.Content ?? string.Empty,
                    trace:  traceSnapshot,
                    ct:     ct);

                response = GroundDocumentSources(response, documentSourceFilesOrdered, documentSearchRan);

                // Emit structured log events for every LLM interaction
                logger.LogInformation("LLM call completed {@LlmTrace}", new {
                    Model = agentOptions.ModelName,
                    PromptTokens = response.Trace!.TotalPromptTokens,
                    CompletionTokens = response.Trace!.TotalCompletionTokens,
                    DurationMs = sw.ElapsedMilliseconds,
                    ToolCallsRequested = traceSnapshot.ToolCallSequence.Length,
                    CacheHit = false,
                    Iteration = iteration + 1,
                });

                llmCallMetrics.RecordCompletedCall(
                    model: agentOptions.ModelName,
                    promptTokens: response.Trace!.TotalPromptTokens,
                    completionTokens: response.Trace!.TotalCompletionTokens,
                    durationMs: sw.ElapsedMilliseconds,
                    toolCallsRequested: traceSnapshot.ToolCallSequence.Length,
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

            // Tool calls → execute and feed results back 
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
                if (TryCollectDocumentSourceFiles(result, documentSourceFilesOrdered, documentSourceFilesSeen))
                    documentSearchRan = true;
                history.Add(new Message(ChatRole.Tool, result.Content)
                {
                    ToolName = result.ToolName
                });
            }

            // Loop continues — model will see tool results and either call more
            // tools or produce a final answer.
        }

        // Iteration limit exceeded 
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

    public async IAsyncEnumerable<AgentStreamEvent> RunStreamAsync(
        string sessionId,
        string userQuery,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.run.stream");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("gen_ai.request.model", agentOptions.ModelName);

        var sw = Stopwatch.StartNew();
        string resolvedQuery = userQuery;

        logger.LogInformation("Agent streaming query started: SessionId: {SessionId}, Query: {Query}", sessionId, userQuery);

        var persistedTurns = await conversationStore.GetAsync(sessionId, ct);

        // Semantic cache (streaming path still honors cache)
        if (semanticCacheOptions.Enabled)
        {
            resolvedQuery = await queryRewriter.RewriteAsync(userQuery, persistedTurns, ct);
            logger.LogInformation("Resolved query: {ResolvedQuery}", resolvedQuery);

            var cacheResult = await semanticCache.GetAsync(resolvedQuery, ct);
            if (cacheResult.IsHit)
            {
                activity?.SetTag("cache.hit", true);
                yield return new AgentStreamFinal(cacheResult.Value);
                yield break;
            }

            activity?.SetTag("cache.hit", false);
        }

        var systemPrompt = await ComposeSystemPromptAsync(ct);

        var userMessage = new Message(ChatRole.User, userQuery);
        var history = new List<Message>(persistedTurns.Count + 2)
        {
            new(ChatRole.System, systemPrompt)
        };
        history.AddRange(persistedTurns);
        history.Add(userMessage);

        var trace = new AgentTraceBuilder();
        var documentSourceFilesOrdered = new List<string>();
        var documentSourceFilesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documentSearchRan = false;

        for (int iteration = 0; iteration < agentOptions.MaxIterations; iteration++)
        {
            using var iterationActivity = LocalMindActivitySources.Agent.StartActivity("agent.iteration");
            iterationActivity?.SetTag("agent.iteration", iteration);

            logger.LogDebug("ReAct iteration {Iteration}", iteration);

            // We keep the tool-using portion non-streaming to avoid leaking partial thoughts/tool JSON.
            var llmResponse = await CallModelDoneAsync(history, ct);
            var llmDoneResponse = (ChatDoneResponseStream)llmResponse;

            var toolCallNames = llmResponse.Message.ToolCalls?
                .Select(tc => tc.Function?.Name ?? "unknown")
                .ToArray() ?? [];

            trace.RecordIteration(
                promptTokens: llmDoneResponse.PromptEvalCount,
                completionTokens: llmDoneResponse.EvalCount,
                promptEvalDurationMs: llmDoneResponse.PromptEvalDuration / 1_000_000L,
                toolCallNames: toolCallNames);

            history.Add(llmResponse.Message);

            logger.LogInformation("LLM Thinking: {Thinking}", llmResponse.Message.Thinking);

            var toolCalls = llmResponse.Message.ToolCalls?.ToList() ?? [];

            if (toolCalls.Count == 0)
            {
                // Re-run the final answer as a streaming call and emit chunks.
                history.RemoveAt(history.Count - 1); // remove the non-streaming assistant message

                var streamedText = new StringBuilder();
                ChatDoneResponseStream? streamedDone = null;
                var answerExtractor = new JsonAnswerStreamExtractor();

                await foreach (var chunk in CallModelStreamAsync(history, ct))
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
                        $"Ollama returned no response for model '{agentOptions.ModelName}'. " +
                        "Is the model pulled? Run: ollama pull " + agentOptions.ModelName);

                // Ensure the message we add to history has the complete content.
                streamedDone.Message.Content = streamedText.ToString();
                history.Add(streamedDone.Message);

                sw.Stop();
                var traceSnapshot = trace.Build(sw.Elapsed);

                var response = await structuredOutputParser.ParseFinalResponseAsync(
                    raw: streamedDone.Message.Content ?? string.Empty,
                    trace: traceSnapshot,
                    ct: ct);

                response = GroundDocumentSources(response, documentSourceFilesOrdered, documentSearchRan);

                logger.LogInformation("LLM call completed {@LlmTrace}", new {
                    Model = agentOptions.ModelName,
                    PromptTokens = response.Trace!.TotalPromptTokens,
                    CompletionTokens = response.Trace!.TotalCompletionTokens,
                    DurationMs = sw.ElapsedMilliseconds,
                    ToolCallsRequested = traceSnapshot.ToolCallSequence.Length,
                    CacheHit = false,
                    Iteration = iteration + 1,
                });

                llmCallMetrics.RecordCompletedCall(
                    model: agentOptions.ModelName,
                    promptTokens: response.Trace!.TotalPromptTokens,
                    completionTokens: response.Trace!.TotalCompletionTokens,
                    durationMs: sw.ElapsedMilliseconds,
                    toolCallsRequested: traceSnapshot.ToolCallSequence.Length,
                    cacheHit: false,
                    iteration: iteration + 1);

                if (semanticCacheOptions.Enabled)
                    await semanticCache.SetAsync(resolvedQuery, response, ct);

                await conversationStore.AppendTurnAsync(
                    sessionId,
                    userMessage,
                    new Message(ChatRole.Assistant, streamedDone.Message.Content ?? string.Empty),
                    ct);

                yield return new AgentStreamFinal(response);
                yield break;
            }

            var toolResults = await executor.ExecuteAllAsync([llmResponse.Message], ct);
            foreach (var result in toolResults)
            {
                if (TryCollectDocumentSourceFiles(result, documentSourceFilesOrdered, documentSourceFilesSeen))
                    documentSearchRan = true;
                history.Add(new Message(ChatRole.Tool, result.Content)
                {
                    ToolName = result.ToolName
                });
            }
        }

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

    /// <summary>
    /// Tool-use policy sits above the agent output contract and above individual
    /// tool descriptions, so routing ("which tools, how many") is not duplicated
    /// inside each function prompt.
    /// </summary>
    private async Task<string> ComposeSystemPromptAsync(CancellationToken ct)
    {
        var toolPolicy = await prompts.GetAsync(PromptNames.ToolPolicy, ct: ct);
        var agentPrompt = await prompts.GetAsync(PromptNames.KnowledgeAgent, ct: ct);
        return $"{toolPolicy.Content}\n\n{agentPrompt.Content}";
    }

    private async Task<ChatResponseStream> CallModelDoneAsync(
        List<Message> history,
        CancellationToken ct)
    {
        using var activity = LocalMindActivitySources.Agent.StartActivity("agent.llm.chat");
        activity?.SetTag("gen_ai.request.model", agentOptions.ModelName);

        var request = new ChatRequest
        {
            Model    = agentOptions.ModelName,
            Messages = history,
            Tools    = manifest.Build(),
            Stream   = false
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

    private IAsyncEnumerable<ChatResponseStream> CallModelStreamAsync(
        List<Message> history,
        CancellationToken ct)
    {
        var request = new ChatRequest
        {
            Model = agentOptions.ModelName,
            Messages = history,
            Tools = manifest.Build(),
            Stream = true
        };

        return ollama.ChatAsync(request, ct)
            .Where(static chunk => chunk is not null)!
            .Select(static chunk => chunk!);
    }

    /// <summary>
    /// When the model is instructed to output a JSON object (for structured parsing),
    /// stream only the raw human-facing <c>answer</c> string to the user.
    /// </summary>
    private sealed class JsonAnswerStreamExtractor
    {
        // We incrementally scan for the JSON key "answer" and then emit the value string,
        // unescaping JSON string escapes as we go.
        private enum State
        {
            SeekKey,
            AfterKey,        // matched "answer"
            SeekColon,
            SeekValueStart,  // seek opening quote of the value string
            InValue,
            Escape,
            Unicode1,
            Unicode2,
            Unicode3,
            Unicode4
        }

        private static ReadOnlySpan<char> Key => "\"answer\"";

        private State state = State.SeekKey;
        private int keyMatchIdx = 0;
        private int unicode = 0;

        public IEnumerable<string> Push(string input)
        {
            if (string.IsNullOrEmpty(input))
                yield break;

            var sb = new StringBuilder();

            foreach (var ch in input)
            {
                switch (state)
                {
                    case State.SeekKey:
                    {
                        if (ch == Key[keyMatchIdx])
                        {
                            keyMatchIdx++;
                            if (keyMatchIdx == Key.Length)
                            {
                                state = State.SeekColon;
                                keyMatchIdx = 0;
                            }
                        }
                        else
                        {
                            keyMatchIdx = ch == Key[0] ? 1 : 0;
                        }

                        break;
                    }

                    case State.SeekColon:
                    {
                        if (char.IsWhiteSpace(ch))
                            break;
                        if (ch == ':')
                            state = State.SeekValueStart;
                        else
                            state = State.SeekKey; // malformed/unexpected; restart scan
                        break;
                    }

                    case State.SeekValueStart:
                    {
                        if (char.IsWhiteSpace(ch))
                            break;
                        if (ch == '"')
                            state = State.InValue;
                        else
                            state = State.SeekKey; // value isn't a string; restart scan
                        break;
                    }

                    case State.InValue:
                    {
                        if (ch == '\\')
                        {
                            state = State.Escape;
                            break;
                        }

                        if (ch == '"')
                        {
                            // End of answer string.
                            state = State.SeekKey;
                            break;
                        }

                        sb.Append(ch);
                        break;
                    }

                    case State.Escape:
                    {
                        state = State.InValue;
                        switch (ch)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                unicode = 0;
                                state = State.Unicode1;
                                break;
                            default:
                                // Unknown escape — best effort: emit as-is.
                                sb.Append(ch);
                                break;
                        }

                        break;
                    }

                    case State.Unicode1:
                    case State.Unicode2:
                    case State.Unicode3:
                    case State.Unicode4:
                    {
                        if (!TryHex(ch, out var val))
                        {
                            state = State.InValue;
                            break;
                        }

                        unicode = (unicode << 4) | val;

                        state = state switch
                        {
                            State.Unicode1 => State.Unicode2,
                            State.Unicode2 => State.Unicode3,
                            State.Unicode3 => State.Unicode4,
                            _ => State.InValue
                        };

                        if (state == State.InValue)
                        {
                            sb.Append((char)unicode);
                        }

                        break;
                    }
                }

                if (sb.Length >= 256)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }

        private static bool TryHex(char ch, out int value)
        {
            if (ch is >= '0' and <= '9')
            {
                value = ch - '0';
                return true;
            }
            if (ch is >= 'a' and <= 'f')
            {
                value = 10 + (ch - 'a');
                return true;
            }
            if (ch is >= 'A' and <= 'F')
            {
                value = 10 + (ch - 'A');
                return true;
            }

            value = 0;
            return false;
        }
    }

    /// <summary>
    /// Models often hallucinate plausible filenames in <c>sources</c>. When a document-search tool ran,
    /// ground markdown-like entries using filenames from the tool JSON. Non-markdown source strings (e.g. DB tables)
    /// from the model are kept when they do not duplicate grounded files.
    /// </summary>
    private static AgentResponse GroundDocumentSources(
        AgentResponse response,
        List<string> documentSourceFilesOrdered,
        bool documentSearchRan)
    {
        if (!documentSearchRan)
            return response;

        static bool LooksLikeMarkdownFile(string s) =>
            s.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        var nonMdFromModel = response.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s) && !LooksLikeMarkdownFile(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (documentSourceFilesOrdered.Count > 0)
        {
            var merged = new List<string>(documentSourceFilesOrdered);
            foreach (var s in nonMdFromModel)
            {
                if (!merged.Exists(t => t.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    merged.Add(s);
            }

            return response with { Sources = [.. merged] };
        }

        // Document search ran but no filenames parsed — drop invented .md / .markdown only.
        return response with { Sources = [.. nonMdFromModel] };
    }

    /// <summary>
    /// Collects document filenames from any successful tool result whose JSON looks like
    /// ranked document hits (array of objects with filename/file/source). Returns true when
    /// the payload matched that shape, including an empty hit list.
    /// </summary>
    private static bool TryCollectDocumentSourceFiles(
        ToolResult result,
        List<string> ordered,
        HashSet<string> seen)
    {
        if (!result.IsSuccess)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(result.Content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var matchedDocumentHits = false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object || !LooksLikeDocumentHit(el))
                    continue;

                matchedDocumentHits = true;

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

            // Empty array is a valid no-hit document search; other tools rarely return [].
            return matchedDocumentHits || doc.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            // Malformed tool JSON — leave sources unchanged for this result.
            return false;
        }
    }

    private static bool LooksLikeDocumentHit(JsonElement el) =>
        HasStringProperty(el, "filename")
        || HasStringProperty(el, "file")
        || HasStringProperty(el, "source");

    private static bool HasStringProperty(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String;
}