namespace LocalMind.Agent;

/// <summary>
/// Configuration for the Agent. Bind from appsettings.json:
///
///   "Agent": {
///     "ModelName": "qwen3",
///     "MaxIterations": 8,
///     "MaxOutputRetries": 3,
///     "SemanticCacheThreshold": 0.92,
///     "EnableSemanticCache": true
///   }
///
///   services.Configure&lt;AgentOptions&gt;(config.GetSection("Agent"));
/// </summary>
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
    /// Cosine similarity threshold for semantic cache hits.
    /// 0.92 is a reasonable default — lower values risk false hits (different
    /// questions getting the same cached answer), higher values miss obvious paraphrases.
    /// Tune this in Phase 4 experiments.
    /// </summary>
    public float SemanticCacheThreshold { get; set; } = 0.92f;

    /// <summary>Set to false to bypass the semantic cache entirely (useful during development).</summary>
    public bool EnableSemanticCache { get; set; } = true;
}