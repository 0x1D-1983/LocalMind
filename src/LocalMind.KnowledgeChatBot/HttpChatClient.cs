using System.Net.Http.Json;
using LocalMind.Agent;
using LocalMind.Application.Chat;

namespace LocalMind.KnowledgeChatBot;

internal sealed class HttpChatClient(HttpClient http) : IChatClient
{
    public async Task<ChatResponse> SendAsync(string conversationId, string message, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            "api/chat",
            new ChatRequest
            {
                Message = message,
                ConversationId = conversationId
            },
            ct);

        if (response.StatusCode == System.Net.HttpStatusCode.BadGateway)
        {
            var problem = await response.Content.ReadFromJsonAsync<HttpProblem>(ct);
            throw new AgentException(problem?.Detail ?? "Agent failed.");
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        return body ?? throw new InvalidOperationException("API returned an empty chat response.");
    }

    private sealed class HttpProblem
    {
        public string? Detail { get; set; }
    }
}
