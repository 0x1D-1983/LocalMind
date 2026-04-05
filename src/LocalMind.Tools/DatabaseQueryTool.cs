using System.Text.Json.Nodes;
using LocalMind.Tools;


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