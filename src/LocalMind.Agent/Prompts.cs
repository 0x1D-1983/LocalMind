namespace LocalMind.Agent;

public static class Prompts
{
    // ─────────────────────────────────────────────────────────────────────────
    // Public: system prompt
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
    public static string SystemPrompt => """
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
          call search_knowledge_base first (use top_k 18–25 for precise facts: names, relationships, dates).
          Put the subject's name and the kind of fact in the search query (e.g. "Jean Grey parents family"), not a single vague word.
          Newly ingested documents are only visible through that tool — do not rely on memory from earlier in the chat for KB content.
        - If multiple tools give independent information, combine them in your answer.
        - If you cannot answer, set confidence to 0.0 and explain in "answer".
        - Never invent data — only report what the tools return.
        - In "sources", list ONLY file or table names that actually appeared in tool results. Never invent filenames.
        - confidence 1.0 = fully grounded in tool results, 0.5 = partially inferred,
          0.0 = unable to answer.

        Example valid response:
        {"answer":"The payments team owns 3 services with SLAs under 200ms.","sources":["services table"],"confidence":0.95,"tools_used":["query_database"]}
        """;
}