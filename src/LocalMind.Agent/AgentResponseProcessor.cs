using System.Text.Json;
using LocalMind.Tools;

namespace LocalMind.Agent;

/// <summary>
/// Post-processes model output: ground <c>sources</c> from tool payloads and
/// collect document filenames during the tool loop.
/// </summary>
public sealed class AgentResponseProcessor
{
    public AgentResponse GroundSources(
        AgentResponse response,
        IReadOnlyList<string> documentSourceFilesOrdered,
        bool documentSearchRan)
    {
        if (!documentSearchRan)
            return response;

        static bool LooksLikeMarkdownFile(string s) =>
            s.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        var nonMdFromModel = response.Sources
            .Where(s => !string.IsNullOrWhiteSpace(s) && !LooksLikeMarkdownFile(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (documentSourceFilesOrdered.Count > 0)
        {
            var merged = new List<string>(documentSourceFilesOrdered);
            foreach (var s in nonMdFromModel)
            {
                if (!merged.Exists(t => t.Equals(s, StringComparison.OrdinalIgnoreCase)))
                    merged.Add(s);
            }

            return response with { Sources = [.. merged] };
        }

        return response with { Sources = [.. nonMdFromModel] };
    }

    public bool TryCollectDocumentSourceFiles(
        ToolResult result,
        List<string> ordered,
        HashSet<string> seen)
    {
        if (!result.IsSuccess)
            return false;

        try
        {
            using var doc = JsonDocument.Parse(result.Content);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var matchedDocumentHits = false;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object || !LooksLikeDocumentHit(el))
                    continue;

                matchedDocumentHits = true;

                var file = "";
                if (el.TryGetProperty("filename", out var fn) && fn.ValueKind == JsonValueKind.String)
                    file = fn.GetString() ?? "";
                if (string.IsNullOrEmpty(file)
                    && el.TryGetProperty("file", out var f)
                    && f.ValueKind == JsonValueKind.String)
                    file = f.GetString() ?? "";
                if (string.IsNullOrEmpty(file)
                    && el.TryGetProperty("source", out var s)
                    && s.ValueKind == JsonValueKind.String)
                {
                    var path = s.GetString();
                    if (!string.IsNullOrEmpty(path))
                        file = Path.GetFileName(path);
                }

                if (string.IsNullOrEmpty(file) || !seen.Add(file))
                    continue;
                ordered.Add(file);
            }

            return matchedDocumentHits || doc.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeDocumentHit(JsonElement el) =>
        HasStringProperty(el, "filename")
        || HasStringProperty(el, "file")
        || HasStringProperty(el, "source");

    private static bool HasStringProperty(JsonElement el, string name) =>
        el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String;
}
