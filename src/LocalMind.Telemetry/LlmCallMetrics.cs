namespace LocalMind.Telemetry;

using Prometheus;

public sealed class LlmCallMetrics
{
    private readonly Counter _callsTotal = Metrics.CreateCounter(
        "localmind_agent_llm_calls_total",
        "Total number of completed LocalMind agent LLM calls.",
        new CounterConfiguration
        {
            LabelNames = ["model", "cache_hit"]
        });

    private readonly Counter _tokensTotal = Metrics.CreateCounter(
        "localmind_agent_llm_tokens_total",
        "Total tokens consumed by LocalMind agent LLM calls.",
        new CounterConfiguration
        {
            LabelNames = ["model", "token_type"]
        });

    private readonly Histogram _durationMs = Metrics.CreateHistogram(
        "localmind_agent_llm_duration_ms",
        "End-to-end LocalMind agent LLM call latency in milliseconds.",
        new HistogramConfiguration
        {
            LabelNames = ["model", "cache_hit"],
            Buckets = Histogram.ExponentialBuckets(start: 25, factor: 2, count: 10)
        });

    private readonly Histogram _toolCallsRequested = Metrics.CreateHistogram(
        "localmind_agent_llm_tool_calls_requested",
        "Number of tool calls requested before LocalMind agent response completion.",
        new HistogramConfiguration
        {
            LabelNames = ["model"],
            Buckets = Histogram.LinearBuckets(start: 0, width: 1, count: 11)
        });

    private readonly Histogram _iteration = Metrics.CreateHistogram(
        "localmind_agent_llm_iteration",
        "ReAct loop iteration count when LocalMind agent completed.",
        new HistogramConfiguration
        {
            LabelNames = ["model"],
            Buckets = Histogram.LinearBuckets(start: 1, width: 1, count: 10)
        });

    public void RecordCompletedCall(
        string model,
        int promptTokens,
        int completionTokens,
        long durationMs,
        int toolCallsRequested,
        bool cacheHit,
        int iteration)
    {
        var cacheHitLabel = cacheHit ? "true" : "false";

        _callsTotal.WithLabels(model, cacheHitLabel).Inc();
        _tokensTotal.WithLabels(model, "prompt").Inc(promptTokens);
        _tokensTotal.WithLabels(model, "completion").Inc(completionTokens);
        _durationMs.WithLabels(model, cacheHitLabel).Observe(durationMs);
        _toolCallsRequested.WithLabels(model).Observe(toolCallsRequested);
        _iteration.WithLabels(model).Observe(iteration);
    }
}
