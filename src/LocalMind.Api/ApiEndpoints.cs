using LocalMind.Application.Agents;
using LocalMind.Application.Chat;
using LocalMind.Application.Conversations;
using LocalMind.Application.Knowledge;

namespace LocalMind.Api;

internal static class ApiEndpoints
{
    public static WebApplication MapLocalMindApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/chat", async (
            ChatRequest request,
            IChatService chat,
            CancellationToken ct) =>
        {
            var response = await chat.ExecuteAsync(request, ct);
            return Results.Ok(response);
        })
        .WithName("Chat")
        .WithTags("Chat");

        api.MapPost("/agents/{agent}/invoke", async (
            string agent,
            AgentInvokeRequest request,
            IAgentInvokeService agents,
            CancellationToken ct) =>
        {
            var response = await agents.InvokeAsync(agent, request, ct);
            return Results.Ok(response);
        })
        .WithName("InvokeAgent")
        .WithTags("Agents");

        api.MapGet("/conversations/{id}", async (
            string id,
            IConversationService conversations,
            CancellationToken ct) =>
        {
            var conversation = await conversations.GetAsync(id, ct);
            return conversation is null ? Results.NotFound() : Results.Ok(conversation);
        })
        .WithName("GetConversation")
        .WithTags("Conversations");

        api.MapPost("/knowledge/documents", IngestDocumentAsync)
            .DisableAntiforgery()
            .WithName("IngestDocument")
            .WithTags("Knowledge");

        return app;
    }

    private static async Task<IResult> IngestDocumentAsync(
        HttpRequest http,
        IKnowledgeDocumentService documents,
        CancellationToken ct)
    {
        var request = await ReadIngestRequestAsync(http, ct);
        if (request is null)
            return Results.BadRequest(new
            {
                error = "Send application/json ({ fileName, content }), multipart/form-data with a file, or a raw document body with a fileName query parameter or Content-Disposition filename."
            });

        var response = await documents.IngestAsync(request, ct);
        return Results.Ok(response);
    }

    private static async Task<IngestDocumentRequest?> ReadIngestRequestAsync(HttpRequest http, CancellationToken ct)
    {
        if (http.HasFormContentType)
        {
            var form = await http.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null)
                return null;

            using var reader = new StreamReader(file.OpenReadStream());
            var content = await reader.ReadToEndAsync(ct);
            return new IngestDocumentRequest
            {
                FileName = file.FileName,
                Content = content,
                Source = file.FileName
            };
        }

        if (http.HasJsonContentType())
            return await http.ReadFromJsonAsync<IngestDocumentRequest>(cancellationToken: ct);

        var fileName = GetRawUploadFileName(http);
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        using var bodyReader = new StreamReader(http.Body);
        var body = await bodyReader.ReadToEndAsync(ct);
        return new IngestDocumentRequest
        {
            FileName = fileName,
            Content = body,
            Source = fileName
        };
    }

    private static string? GetRawUploadFileName(HttpRequest http)
    {
        var fromQuery = http.Query["fileName"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fromQuery))
            return Path.GetFileName(fromQuery.Trim());

        var disposition = http.GetTypedHeaders().ContentDisposition;
        var fromHeader = disposition?.FileNameStar.Value ?? disposition?.FileName.Value;
        if (!string.IsNullOrWhiteSpace(fromHeader))
            return Path.GetFileName(fromHeader.Trim());

        return MediaTypeFileName(http.ContentType);
    }

    private static string? MediaTypeFileName(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.ToLowerInvariant() switch
        {
            "text/markdown" or "text/x-markdown" => "document.md",
            "text/plain" => "document.txt",
            "text/html" => "document.html",
            "application/octet-stream" => "document.bin",
            _ => null
        };
    }
}
