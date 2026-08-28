namespace LocalMind.Application.Chat;

public interface IChatService
{
    Task<ChatResponse> ExecuteAsync(ChatRequest request, CancellationToken ct = default);
}
