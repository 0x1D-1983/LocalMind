using OllamaSharp;
using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;
using static OllamaSharp.Models.Chat.Message;

namespace LocalMind.Agent;

public class Agent(OllamaApiClient ollama, ToolExecutor executor)
{
    private const int MaxIterations = 8;

    public async Task<AgentResponse> RunAsync(string userQuery)
    {
        var messages = new List<Message>
        {
            new(Role.System, BuildSystemPrompt()),
            new(Role.User, userQuery)
        };

        for (int i = 0; i < MaxIterations; i++)
        {
            var response = await ollama.ChatAsync(new ChatRequest
            {
                Model = "qwen3",
                Messages = messages,
                Tools = GetToolDefinitions(),  // Always send tool defs
                Stream = false
            });

            messages.Add(response.Message);

            // No tool calls → model has a final answer
            if (response.Message.ToolCalls is not { Count: > 0 })
                return ParseFinalResponse(response.Message.Content);

            // Execute ALL tool calls (potentially in parallel)
            var toolResults = await ExecuteToolCallsAsync(response.Message.ToolCalls);

            // Add all results back to the conversation
            foreach (var result in toolResults)
                messages.Add(new Message(Role.Tool, result.Content) { Name = result.ToolName });
        }

        throw new InvalidOperationException("Agent exceeded max iterations");
    }

    private async Task<ToolResult[]> ExecuteToolCallsAsync(IList<ToolCall> calls)
    {
        // PARALLEL execution — this is the key optimisation
        var tasks = calls.Select(call => executor.ExecuteAsync(call));
        return await Task.WhenAll(tasks);
    }
}