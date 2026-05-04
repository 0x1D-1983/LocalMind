using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;

namespace LocalMind.Agent;

public class QueryRewriter(OllamaApiClient ollama, QueryRewriterOptions options)
{
    public async Task<string> RewriteAsync(
        string query,
        IReadOnlyList<Message> history,
        CancellationToken ct = default)
    {
        if (!history.Any()) return query; // no context to resolve

        var prompt = $"""
            Given this conversation history:
            {FormatHistory(history)}
            
            Rewrite the following question as a fully self-contained,
            context-free question. Resolve all pronouns and references.
            Return only the rewritten question, nothing else.
            
            Question: {query}
            """;

        var response = await ollama.GenerateAsync(new GenerateRequest {
            Model   = options.Model,
            Prompt  = prompt,
            Stream  = false
        }, ct).LastAsync(ct);

        return response?.Response?.Trim() ?? string.Empty;
    }

    private string FormatHistory(IReadOnlyList<Message> history)
    {
        return string.Join("\n", history.Select(m => $"{m.Role}: {m.Content}\n"));
    }
}