// ─────────────────────────────────────────────────────────────────────────────
// Example: CalculatorTool — a complete, minimal tool implementation.
// Shows the full pattern: schema definition, input parsing, error handling.
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json.Nodes;
using LocalMind.Tools;


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
