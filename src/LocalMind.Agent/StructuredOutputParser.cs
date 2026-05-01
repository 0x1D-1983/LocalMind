using System.Text.Json;
using LocalMind.Ollama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

public interface IStructuredOutputParser
{
    Task<AgentResponse> ParseFinalResponseAsync(
        string raw,
        AgentTrace trace,
        CancellationToken ct);
}

public sealed class StructuredOutputParser : IStructuredOutputParser
{
    private readonly OllamaApiClient _ollama;
    private readonly AgentOptions _options;
    private readonly ILogger<StructuredOutputParser> _logger;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public StructuredOutputParser(
        OllamaApiClient ollama,
        IOptions<AgentOptions> options,
        ILogger<StructuredOutputParser> logger)
    {
        _ollama = ollama;
        _options = options.Value;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public: structured output parsing
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
    public async Task<AgentResponse> ParseFinalResponseAsync(
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
                new(ChatRole.System, Prompts.SystemPrompt),
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