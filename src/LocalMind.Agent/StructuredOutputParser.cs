// using System.Text.Json;

// namespace LocalMind.Agent;

// public class StructuredOutputParser<T>
// {
//     private const int MaxRetries = 3;

//     public async Task<T> ParseWithRetryAsync(
//         Func<string, Task<string>> llmCall,
//         string prompt,
//         JsonSerializerOptions? options = null)
//     {
//         string? lastError = null;

//         for (int attempt = 0; attempt < MaxRetries; attempt++)
//         {
//             var augmentedPrompt = attempt == 0
//                 ? prompt
//                 : $"{prompt}\n\nYour previous response was invalid JSON: {lastError}\nFix it and respond with ONLY valid JSON.";

//             var raw = await llmCall(augmentedPrompt);

//             // Strip markdown fences if model misbehaves
//             var cleaned = StripFences(raw);

//             try
//             {
//                 return JsonSerializer.Deserialize<T>(cleaned, options)
//                     ?? throw new JsonException("Deserialized to null");
//             }
//             catch (JsonException ex)
//             {
//                 lastError = ex.Message;
//                 // TODO: Log attempt failure with Serilog
//             }
//         }

//         throw new InvalidOperationException(
//             $"Model failed to produce valid JSON after {MaxRetries} attempts");
//     }

//     private static string StripFences(string raw) => raw.Trim().TrimStart('`').TrimStart("json").Trim('`').Trim().ToString();
// }