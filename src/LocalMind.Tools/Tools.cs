using System.Text.Json.Nodes;

namespace LocalMind.Tools;

// The three tools
public static class Tools
{
    public static readonly ToolDefinition SearchKnowledge = new(
        Name: "search_knowledge_base",
        Description: """
            Searches the local documentation knowledge base using semantic similarity.
            Use this when the user asks about architecture, processes, or concepts
            that would be found in documentation. Do NOT use for live data or counts.
            """,
        InputSchema: (JsonObject)JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "query": {
                  "type": "string",
                  "description": "Natural-language search query for the documentation index."
                },
                "top_k": {
                  "type": "integer",
                  "description": "Maximum number of chunks to return.",
                  "minimum": 1,
                  "maximum": 20
                }
              },
              "required": ["query"]
            }
            """
        )!
    );

    public static readonly ToolDefinition QueryDatabase = new(
        Name: "query_database",
        Description: """
            Executes a read-only SQL query against the local SQLite database.
            Use this for structured data: counts, lists, lookups, aggregations.
            The database has tables: services(id, name, team, sla_ms),
            incidents(id, service_id, severity, resolved_at).
            ONLY SELECT statements are permitted.
            """,
        InputSchema: (JsonObject)JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "sql": {
                  "type": "string",
                  "description": "A single read-only SELECT statement."
                }
              },
              "required": ["sql"]
            }
            """
        )!
    );

    public static readonly ToolDefinition Calculate = new(
        Name: "calculate",
        Description: """
            Evaluates a mathematical expression. Use for any arithmetic
            the model should not do in its head (averages, percentages, etc).
            """,
        InputSchema: (JsonObject)JsonNode.Parse(
            """
            {
              "type": "object",
              "properties": {
                "expression": {
                  "type": "string",
                  "description": "Arithmetic expression to evaluate (e.g. (a + b) / 2)."
                }
              },
              "required": ["expression"]
            }
            """
        )!
    );
}
