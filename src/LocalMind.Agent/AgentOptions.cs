using System.ComponentModel.DataAnnotations;

namespace LocalMind.Agent;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>
    /// Ollama model tag. Must support tool use — qwen3, llama3.1, mistral-nemo etc.
    /// Verify with: ollama show &lt;model&gt; --modelfile | grep tool
    /// </summary>
    public string ModelName { get; set; } = "qwen3";

    /// <summary>
    /// Hard cap on ReAct loop iterations before throwing.
    /// Each iteration is at minimum one round-trip to Ollama, plus tool execution time.
    /// 8 is generous for most queries; complex multi-hop reasoning rarely needs more than 5.
    /// </summary>
    public int MaxIterations { get; set; } = 8;

    /// <summary>
    /// How many times to retry structured output parsing when the model returns
    /// invalid or non-conforming JSON. Each retry feeds the validation error
    /// back into the prompt so the model can self-correct.
    /// </summary>
    public int MaxOutputRetries { get; set; } = 3;

    /// <summary>
    /// Number of user/assistant turn pairs to retain per session.
    /// Older turns are evicted (sliding window).
    /// </summary>
    [Range(1, 100)]
    public int MaxConversationTurns { get; set; } = 20;
}