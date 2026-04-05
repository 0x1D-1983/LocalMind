using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using OllamaSharp.Models.Chat;
using static OllamaSharp.Models.Chat.Message;

namespace LocalMind.Tools;

/// <summary>
/// Dispatches tool calls emitted by the model to registered ITool implementations.
///
/// Key behaviours:
///   - Parallel execution via Task.WhenAll — independent tool calls run concurrently.
///   - Per-call isolation — one failing tool does not abort the others.
///   - Input validation before handing off to the tool implementation.
///   - Structured logging on every call for Grafana / Loki observability.
///   - Unknown tool names return a ToolResult.Fail so the model can self-correct,
///     rather than throwing and crashing the agent loop.
/// </summary>
public sealed class ToolExecutor
{
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(IToolRegistry registry, ILogger<ToolExecutor> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Execute all tool calls in the model's response concurrently.
    /// Results are returned in the same order as the input list.
    /// </summary>
    public async Task<ToolResult[]> ExecuteAllAsync(
        IReadOnlyList<Message> toolCallMessages,
        CancellationToken ct = default)
    {
        // Extract all tool calls from all messages (model can batch them)
        var calls = toolCallMessages
            .SelectMany(m => m.ToolCalls ?? [])
            .ToList();

        if (calls.Count == 0)
            return [];

        _logger.LogDebug("Dispatching {Count} tool call(s) in parallel: {Names}",
            calls.Count, string.Join(", ", calls.Select(c => c.Function?.Name ?? "unknown")));

        // Fire all calls concurrently — this is the key latency optimisation.
        // Task.WhenAll collects all results even if some fail.
        var tasks = calls.Select(call => ExecuteSingleAsync(call, ct));
        var results = await Task.WhenAll(tasks);

        LogSummary(results);
        return results;
    }

    /// <summary>
    /// Execute a single tool call. Never throws — failures are encoded as ToolResult.Fail.
    /// </summary>
    public async Task<ToolResult> ExecuteSingleAsync(
        ToolCall call,
        CancellationToken ct = default)
    {
        var toolName = call.Function?.Name ?? string.Empty;
        var sw = Stopwatch.StartNew();

        // --- 1. Resolve tool from registry ---
        if (!_registry.TryGet(toolName, out var tool))
        {
            sw.Stop();
            _logger.LogWarning("Unknown tool requested by model: '{ToolName}'", toolName);
            return ToolResult.Fail(
                toolName,
                $"Unknown tool '{toolName}'. Available tools: {string.Join(", ", _registry.All.Select(t => t.Name))}",
                sw.Elapsed);
        }

        // --- 2. Parse and validate input ---
        var parseResult = ParseInput(call);
        if (!parseResult.IsSuccess)
        {
            sw.Stop();
            _logger.LogWarning("Invalid input for tool '{ToolName}': {Error}", toolName, parseResult.Error);
            return ToolResult.Fail(toolName, parseResult.Error!, sw.Elapsed);
        }

        // --- 3. Validate required fields against tool's schema ---
        var validationError = ValidateRequiredFields(parseResult.Input!, tool.InputSchema);
        if (validationError is not null)
        {
            sw.Stop();
            _logger.LogWarning("Schema validation failed for '{ToolName}': {Error}", toolName, validationError);
            return ToolResult.Fail(toolName, validationError, sw.Elapsed);
        }

        // --- 4. Execute ---
        try
        {
            _logger.LogDebug("Executing tool '{ToolName}' with input: {Input}",
                toolName, parseResult.Input!.ToJsonString());

            var result = await tool.ExecuteAsync(parseResult.Input!, ct);
            sw.Stop();

            _logger.LogInformation(
                "Tool '{ToolName}' completed in {ElapsedMs}ms — Success: {IsSuccess}",
                toolName, sw.ElapsedMilliseconds, result.IsSuccess);

            // Stamp elapsed on the result (tool impls return their own but we override
            // with the wall-clock time measured here for consistency)
            return result with { Elapsed = sw.Elapsed };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning("Tool '{ToolName}' was cancelled after {ElapsedMs}ms",
                toolName, sw.ElapsedMilliseconds);
            return ToolResult.Fail(toolName, "Tool execution was cancelled.", sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Log the full exception for diagnostics but return a safe message to the model.
            // Never leak stack traces or connection strings into the LLM context.
            _logger.LogError(ex, "Tool '{ToolName}' threw an unhandled exception after {ElapsedMs}ms",
                toolName, sw.ElapsedMilliseconds);

            return ToolResult.Fail(
                toolName,
                $"Tool '{toolName}' encountered an internal error. Try rephrasing your input.",
                sw.Elapsed);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static ParseResult ParseInput(ToolCall call)
    {
        // OllamaSharp surfaces arguments as IDictionary<string, object?>; project to JsonObject.
        var arguments = call.Function?.Arguments;

        if (arguments is null)
        {
            // Some tools have no required inputs — empty object is valid.
            return ParseResult.Success(new JsonObject());
        }

        try
        {
            return ParseResult.Success(ArgumentsToJsonObject(arguments));
        }
        catch (Exception ex)
        {
            return ParseResult.Failure($"Could not convert tool arguments to JSON: {ex.Message}");
        }
    }

    private static JsonObject ArgumentsToJsonObject(IDictionary<string, object?> arguments)
    {
        var obj = new JsonObject();
        foreach (var kv in arguments)
            obj[kv.Key] = ValueToJsonNode(kv.Value);
        return obj;
    }

    private static JsonNode? ValueToJsonNode(object? value)
    {
        if (value is null)
            return null;

        if (value is JsonNode node)
            return node;

        if (value is JsonElement el)
            return JsonNode.Parse(el.GetRawText());

        if (value is IDictionary<string, object?> nested)
            return ArgumentsToJsonObject(nested);

        return JsonSerializer.SerializeToNode(value);
    }

    /// <summary>
    /// Light-touch validation: checks that every field marked required in the schema
    /// is present in the input. Does not do type checking — the tool impl handles that.
    ///
    /// This catches the most common model mistake (omitting a required field) and
    /// returns a clear error message so the model can retry with the correct input.
    /// </summary>
    private static string? ValidateRequiredFields(JsonObject input, JsonObject schema)
    {
        if (!schema.TryGetPropertyValue("required", out var requiredNode))
            return null; // No required fields declared — all inputs optional.

        if (requiredNode is not JsonArray requiredArray)
            return null;

        var missing = requiredArray
            .Select(n => n?.GetValue<string>())
            .Where(field => field is not null && !input.ContainsKey(field))
            .ToList();

        if (missing.Count == 0)
            return null;

        return $"Missing required field(s): {string.Join(", ", missing)}. " +
               $"Provided fields: {string.Join(", ", input.Select(kv => kv.Key))}";
    }

    private void LogSummary(ToolResult[] results)
    {
        var failed = results.Count(r => !r.IsSuccess);
        var totalMs = results.Sum(r => r.Elapsed.TotalMilliseconds);
        var maxMs = results.Max(r => r.Elapsed.TotalMilliseconds);

        // Wall-clock is ~maxMs due to parallelism, not totalMs.
        _logger.LogInformation(
            "Tool batch complete — {Total} call(s), {Failed} failed. " +
            "Wall-clock ≈{MaxMs:F0}ms (sum {TotalMs:F0}ms, saved {SavedMs:F0}ms by parallelism)",
            results.Length, failed, maxMs, totalMs, totalMs - maxMs);
    }

    // -------------------------------------------------------------------------
    // Inner types
    // -------------------------------------------------------------------------

    private sealed record ParseResult(bool IsSuccess, JsonObject? Input, string? Error)
    {
        public static ParseResult Success(JsonObject input) => new(true, input, null);
        public static ParseResult Failure(string error) => new(false, null, error);
    }
}