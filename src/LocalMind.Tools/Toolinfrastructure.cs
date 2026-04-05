using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace LocalMind.Tools;

// ─────────────────────────────────────────────────────────────────────────────
// DI Registration
// ─────────────────────────────────────────────────────────────────────────────

public static class ToolServiceExtensions
{
    /// <summary>
    /// Registers the tool infrastructure.
    /// Add individual ITool implementations via AddTool&lt;T&gt;() after calling this.
    ///
    /// Usage in Program.cs:
    ///   builder.Services
    ///       .AddToolInfrastructure()
    ///       .AddTool&lt;KnowledgeSearchTool&gt;()
    ///       .AddTool&lt;DatabaseQueryTool&gt;()
    ///       .AddTool&lt;CalculatorTool&gt;();
    /// </summary>
    public static IServiceCollection AddToolInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IToolRegistry, ToolRegistry>();
        services.AddSingleton<ToolExecutor>();
        services.AddSingleton<ToolManifestBuilder>();
        return services;
    }

    /// <summary>Registers a tool implementation by its ITool interface.</summary>
    public static IServiceCollection AddTool<T>(this IServiceCollection services)
        where T : class, ITool
    {
        // Register as both ITool (for IEnumerable<ITool> injection into ToolRegistry)
        // and as T (for direct injection if needed).
        services.AddSingleton<ITool, T>();
        services.AddSingleton<T>(sp => (T)sp.GetServices<ITool>().OfType<T>().First());
        return services;
    }
}


// ─────────────────────────────────────────────────────────────────────────────
// Example: CalculatorTool — a complete, minimal tool implementation.
// Shows the full pattern: schema definition, input parsing, error handling.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Evaluates a mathematical expression string.
/// Deterministic — useful for verifying the model isn't hallucinating numbers.
///
/// The real KnowledgeSearchTool and DatabaseQueryTool live in their own files
/// and follow this same pattern.
/// </summary>
public sealed class CalculatorTool : ITool
{
    public string Name => "calculate";

    public string Description => """
        Evaluates a mathematical expression and returns the result as a number.
        Use this for any arithmetic the model should not perform in its head:
        averages, percentages, compound calculations, unit conversions.
        Supports: +, -, *, /, %, ^, sqrt(), floor(), ceil(), round(), abs().
        Examples: "sqrt(144)", "(42 * 1.2) / 100", "round(3.14159, 2)".
        Do NOT use for logic or string operations.
        """;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["expression"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A mathematical expression to evaluate, e.g. '(10 + 20) * 1.5'"
            }
        },
        ["required"] = new JsonArray { "expression" }
    };

    public Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        // ToolExecutor guarantees 'expression' is present before calling us.
        var expression = input["expression"]?.GetValue<string>() ?? string.Empty;

        try
        {
            // NCalc2 is the recommended expression evaluator for .NET:
            //   dotnet add package NCalc2
            //
            // var calc = new NCalc.Expression(expression);
            // var result = calc.Evaluate();
            // return Task.FromResult(ToolResult.Ok(Name, result.ToString()!, TimeSpan.Zero));

            // ── Placeholder until NCalc2 is added ──
            // For simple expressions, DataTable.Compute works without extra deps:
            var result = new System.Data.DataTable().Compute(expression, null);
            return Task.FromResult(ToolResult.Ok(Name, result?.ToString() ?? "null", TimeSpan.Zero));
        }
        catch (Exception ex)
        {
            // Return structured failure — ToolExecutor will pass this to the model.
            return Task.FromResult(ToolResult.Fail(
                Name,
                $"Could not evaluate expression '{expression}': {ex.Message}",
                TimeSpan.Zero));
        }
    }
}


// ─────────────────────────────────────────────────────────────────────────────
// Tool stub skeletons (flesh out in Phase 3)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Stub — implement in LocalMind.Tools/KnowledgeSearchTool.cs</summary>
public sealed class KnowledgeSearchTool : ITool
{
    public string Name => "search_knowledge_base";

    public string Description => """
        Searches the local documentation knowledge base using semantic similarity.
        Use when the user asks about architecture, processes, or concepts found in docs.
        Do NOT use for live data, counts, or anything requiring a database lookup.
        Returns the top matching document chunks with their source file names.
        """;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Natural language search query"
            },
            ["top_k"] = new JsonObject
            {
                ["type"] = "integer",
                ["description"] = "Number of results to return. Default: 3, max: 10."
            }
        },
        ["required"] = new JsonArray { "query" }
    };

    public Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        // TODO Phase 3: embed query via Nomic, search Qdrant, return top_k chunks
        throw new NotImplementedException("Implement in Phase 3 — Qdrant + Nomic embeddings");
    }
}

/// <summary>Stub — implement in LocalMind.Tools/DatabaseQueryTool.cs</summary>
public sealed class DatabaseQueryTool : ITool
{
    public string Name => "query_database";

    public string Description => """
        Executes a read-only SQL SELECT query against the local SQLite database.
        Use for structured data: counts, lists, lookups, aggregations.
        Tables: services(id, name, team, sla_ms, language),
                incidents(id, service_id, severity, description, occurred_at, resolved_at).
        Only SELECT statements are permitted — any other statement will be rejected.
        """;

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["sql"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A read-only SQL SELECT statement"
            }
        },
        ["required"] = new JsonArray { "sql" }
    };

    public Task<ToolResult> ExecuteAsync(JsonObject input, CancellationToken ct = default)
    {
        // TODO Phase 3: validate SELECT-only, execute via Microsoft.Data.Sqlite,
        // return results as JSON array. Reject anything that isn't a SELECT.
        throw new NotImplementedException("Implement in Phase 3 — SQLite + SQL injection guard");
    }
}