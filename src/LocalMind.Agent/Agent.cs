using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
// using LocalMind.Cache;
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
    private readonly OllamaApiClient    _ollama;
    private readonly ToolExecutor       _executor;
    private readonly ToolManifestBuilder _manifest;
    private readonly SemanticCache      _cache;
    private readonly AgentOptions       _options;
    private readonly ILogger<Agent>     _logger;

    // The system prompt is built once at construction time and reused on every call.
    //
    // WHY THIS MATTERS FOR PERFORMANCE (Phase 5 of the project):
    // Ollama (via llama.cpp) maintains a KV cache. If the start of the prompt is
    // identical across calls, the model skips re-computing those tokens. By keeping
    // the system prompt and tool manifest stable, we get free prefix caching on
    // every call after the first.
    //
    // Rule: stable content (system instructions, tool definitions) goes in the
    // system prompt. Volatile content (retrieved docs, user query, history) goes
    // in the user/assistant turns — never in the system prompt.
    private readonly string _systemPrompt;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public Agent(
        OllamaApiClient ollama,
        ToolExecutor executor,
        ToolManifestBuilder manifest,
        SemanticCache cache,
        IOptions<AgentOptions> options,
        ILogger<Agent> logger)
    {
        _ollama   = ollama;
        _executor = executor;
        _manifest = manifest;
        _cache    = cache;
        _options  = options.Value;
        _logger   = logger;

        _systemPrompt = BuildSystemPrompt();
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
            new(ChatRole.System, _systemPrompt),
            new(ChatRole.User,   userQuery)
        };

        var trace = new AgentTraceBuilder();

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

                var response = await ParseFinalResponseAsync(
                    raw:    llmResponse.Message.Content ?? string.Empty,
                    trace:  trace.Build(sw.Elapsed),
                    ct:     ct);

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

    // ─────────────────────────────────────────────────────────────────────────
    // Private: structured output parsing
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the model's final message content as an AgentResponse JSON object.
    ///
    /// The retry loop feeds the validation error back to the model so it can
    /// self-correct rather than failing hard. This handles the most common
    /// failure mode: the model wrapping its response in markdown code fences
    /// or adding preamble text before the JSON.
    ///
    /// Experiment (Phase 2): Remove the retry loop and observe how often
    /// Qwen3 produces valid JSON on the first attempt with your system prompt.
    /// </summary>
    private async Task<AgentResponse> ParseFinalResponseAsync(
        string raw,
        AgentTrace trace,
        CancellationToken ct)
    {
        string? lastError = null;

        for (int attempt = 0; attempt < _options.MaxOutputRetries; attempt++)
        {
            var contentToParse = attempt == 0
                ? raw
                : await RequestCorrectionAsync(raw, lastError!, ct);

            var cleaned = StripMarkdownFences(contentToParse);

            try
            {
                var response = JsonSerializer.Deserialize<AgentResponse>(cleaned, JsonOptions);

                if (response is null)
                    throw new JsonException("Deserialised to null.");

                ValidateAgentResponse(response);  // semantic checks beyond deserialization

                return response with { Trace = trace };
            }
            catch (JsonException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(
                    "Output parse attempt {Attempt}/{Max} failed: {Error}",
                    attempt + 1, _options.MaxOutputRetries, lastError);
            }
            catch (AgentResponseValidationException ex)
            {
                lastError = ex.Message;
                _logger.LogWarning(
                    "Output validation attempt {Attempt}/{Max} failed: {Error}",
                    attempt + 1, _options.MaxOutputRetries, lastError);
            }
        }

        // All retries exhausted — return a degraded response rather than throwing.
        // The raw content is preserved in the Answer field so the user sees something
        // rather than an unhandled exception.
        _logger.LogError(
            "Structured output parsing failed after {Max} attempts. Raw content: {Raw}",
            _options.MaxOutputRetries, raw);

        return new AgentResponse
        {
            Answer     = $"[Parse failed after {_options.MaxOutputRetries} attempts] {raw}",
            Sources    = [],
            Confidence = 0f,
            ToolsUsed  = trace.ToolCallSequence,
            Trace      = trace
        };
    }

    /// <summary>
    /// When the model produces malformed JSON, re-ask it to fix the output.
    /// Feeding the exact validation error back gives the model a precise signal
    /// to self-correct, which is far more reliable than a generic retry.
    /// </summary>
    private async Task<string> RequestCorrectionAsync(
        string original,
        string validationError,
        CancellationToken ct)
    {
        var correctionRequest = new ChatRequest
        {
            Model  = _options.ModelName,
            Stream = false,
            Messages =
            [
                new(ChatRole.System, _systemPrompt),
                new(ChatRole.User,
                    $"""
                    Your previous response was not valid JSON.

                    Validation error: {validationError}

                    Your response was:
                    {original}

                    Respond ONLY with corrected, valid JSON. No preamble, no fences.
                    """)
            ]
        };

        var response = await _ollama.ChatAsync(correctionRequest, ct)
            .LastOrDefaultAsync(ct);

        return response?.Message.Content ?? string.Empty;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private: system prompt
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the system prompt. Called once at construction and cached.
    ///
    /// Design rules:
    ///   1. Be explicit about the JSON schema — include the exact field names and types.
    ///   2. Use negative examples ("Do NOT wrap in markdown fences") because models
    ///      follow "don't do X" instructions more reliably than "only do Y".
    ///   3. Keep this identical on every call — it's the stable prefix that
    ///      benefits from the model's KV cache.
    /// </summary>
    private static string BuildSystemPrompt() => """
        You are a technical assistant with access to a local knowledge base and database.

        You MUST respond ONLY with a valid JSON object. No preamble. No explanation outside
        the JSON. No markdown fences (no ```json). No trailing text.

        Required JSON schema (all fields mandatory):
        {
          "answer":     string   — your answer to the user's question,
          "sources":    string[] — names of source files or tables you used (empty array if none),
          "confidence": number   — your confidence from 0.0 to 1.0,
          "tools_used": string[] — names of tools you called (empty array if none)
        }

        Rules:
        - Use the available tools to gather facts before answering.
        - For questions about documentation, lore, internal write-ups, or anything that could appear in ingested files,
          call search_knowledge_base first (often with top_k 8–10). Newly ingested documents are only visible through
          that tool — do not rely on memory from earlier in the chat for KB content.
        - If multiple tools give independent information, combine them in your answer.
        - If you cannot answer, set confidence to 0.0 and explain in "answer".
        - Never invent data — only report what the tools return.
        - confidence 1.0 = fully grounded in tool results, 0.5 = partially inferred,
          0.0 = unable to answer.

        Example valid response:
        {"answer":"The payments team owns 3 services with SLAs under 200ms.","sources":["services table"],"confidence":0.95,"tools_used":["query_database"]}
        """;

    // ─────────────────────────────────────────────────────────────────────────
    // Private: helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Strip markdown code fences that models often add despite instructions not to.
    /// Handles: ```json...```, ```...```, and leading/trailing whitespace.
    /// </summary>
    private static string StripMarkdownFences(string raw)
    {
        var trimmed = raw.Trim();

        // Strip ```json...``` or ```...```
        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline > 0)
                trimmed = trimmed[(firstNewline + 1)..];

            if (trimmed.EndsWith("```"))
                trimmed = trimmed[..^3];
        }

        return trimmed.Trim();
    }

    /// <summary>
    /// Semantic validation beyond what JSON deserialisation checks.
    /// JsonSerializer will happily deserialise confidence: "high" as a string
    /// into a float field (as 0) without throwing — these checks catch that.
    /// </summary>
    private static void ValidateAgentResponse(AgentResponse response)
    {
        if (string.IsNullOrWhiteSpace(response.Answer))
            throw new AgentResponseValidationException(
                "'answer' field is empty or whitespace.");

        if (response.Confidence is < 0f or > 1f)
            throw new AgentResponseValidationException(
                $"'confidence' must be between 0.0 and 1.0, got {response.Confidence}.");

        if (response.Sources is null)
            throw new AgentResponseValidationException("'sources' array must not be null.");

        if (response.ToolsUsed is null)
            throw new AgentResponseValidationException("'tools_used' array must not be null.");
    }
}