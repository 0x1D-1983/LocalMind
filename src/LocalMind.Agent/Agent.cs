using System.Diagnostics;
using System.Text.Json;
using LocalMind.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public sealed class Agent
{
    private readonly OllamaApiClient _ollama;
    private readonly ToolExecutor _executor;
    private readonly ToolManifestBuilder _manifest;
    private readonly SemanticCache _cache;
    private readonly AgentOptions _options;
    private readonly ILogger<Agent> _logger;
    private readonly IStructuredOutputParser _structuredOutputParser;

    public Agent(
        ToolExecutor executor,
        ToolManifestBuilder manifest,
        SemanticCache cache,
        IOptions<AgentOptions> options,
        ILogger<Agent> logger,
        IStructuredOutputParser structuredOutputParser,
        OllamaApiClient ollama)
    {
        _ollama = ollama;
        _executor = executor;
        _manifest = manifest;
        _cache    = cache;
        _options  = options.Value;
        _logger   = logger;
        _structuredOutputParser = structuredOutputParser;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AgentResponse> RunAsync(
        string userQuery,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("Agent query started: {Query}", userQuery);

        // ── Phase 1: Semantic cache ───────────────────────────────────────────
        // Check before touching the model — a cache hit costs only one embedding
        // call (~5ms) vs a full agent run (~500ms–5s depending on iterations).
        if (_options.EnableSemanticCache)
        {
            var cached = await _cache.GetAsync(userQuery, ct);
            if (cached is not null)
            {
                sw.Stop();
                _logger.LogInformation(
                    "Cache HIT for query in {ElapsedMs}ms", sw.ElapsedMilliseconds);
                return cached with { FromCache = true };
            }
        }

        // ── Phase 2: Build initial conversation ───────────────────────────────
        // Message ordering for maximum KV cache reuse:
        //   [0] System   — stable: instructions + JSON schema (never changes)
        //   [1] User     — volatile: the query
        //
        // Tool definitions go in the ChatRequest.Tools field, not inline in the
        // system prompt, so Ollama handles their serialisation format correctly.
        var history = new List<Message>
        {
            new(ChatRole.System, Prompts.SystemPrompt),
            new(ChatRole.User,   userQuery)
        };

        var trace = new AgentTraceBuilder();
        var kbSourceFilesOrdered = new List<string>();
        var kbSourceFilesSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Phase 3: ReAct loop ───────────────────────────────────────────────
        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            _logger.LogDebug("ReAct iteration {Iteration}", iteration);

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
                var response = await _structuredOutputParser.ParseFinalResponseAsync(
                    raw:    llmResponse.Message.Content ?? string.Empty,
                    trace:  traceSnapshot,
                    ct:     ct);

                response = GroundKnowledgeSources(response, kbSourceFilesOrdered, traceSnapshot);

                _logger.LogInformation(
                    "Agent completed in {Iterations} iteration(s), {TotalMs}ms, " +
                    "{PromptTokens} prompt tokens, {CompletionTokens} completion tokens",
                    iteration + 1, sw.ElapsedMilliseconds,
                    response.Trace!.TotalPromptTokens,
                    response.Trace!.TotalCompletionTokens);

                // Store in semantic cache for future queries
                if (_options.EnableSemanticCache)
                    await _cache.SetAsync(userQuery, response, ct);

                return response;
            }

            // ── Tool calls → execute and feed results back ────────────────────
            _logger.LogDebug(
                "Iteration {Iteration}: model requested {Count} tool call(s): {Names}",
                iteration, toolCalls.Count, string.Join(", ", toolCallNames));

            // Parallel execution — ToolExecutor.ExecuteAllAsync uses Task.WhenAll.
            // Wall-clock time ≈ slowest single tool, not sum of all tools.
            var toolResults = await _executor.ExecuteAllAsync([llmResponse.Message], ct);

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
        _logger.LogWarning(
            "Agent exceeded max iterations ({Max}) after {ElapsedMs}ms. " +
            "Last history length: {HistoryLength} messages",
            _options.MaxIterations, sw.ElapsedMilliseconds, history.Count);

        throw new AgentException(
            $"Agent could not produce a final answer within {_options.MaxIterations} iterations. " +
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
            Model    = _options.ModelName,
            Messages = history,
            Tools    = _manifest.Build(),
            Stream   = false,
            // Qwen3 has a "thinking" mode that emits <think>...</think> tokens.
            // These count toward your token budget but are useful for complex
            // multi-hop reasoning. Disable by adding: Options = new() { ... }
            // or by appending "/no_think" to the model name in Ollama.
        };

        // LastOrDefaultAsync because with Stream=false, Ollama returns a single
        // chunk containing the complete response. If Stream were true, we'd need
        // to aggregate content across chunks (not done here).
        var response = await _ollama.ChatAsync(request, ct)
            .LastOrDefaultAsync(ct);

        if (response is null)
            throw new AgentException(
                $"Ollama returned no response for model '{_options.ModelName}'. " +
                "Is the model pulled? Run: ollama pull " + _options.ModelName);

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