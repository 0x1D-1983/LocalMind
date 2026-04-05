
// ─────────────────────────────────────────────────────────────────────────────
// Tool stub skeletons (flesh out in Phase 3)
// ─────────────────────────────────────────────────────────────────────────────

using System.Text.Json.Nodes;
using LocalMind.Tools;


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
