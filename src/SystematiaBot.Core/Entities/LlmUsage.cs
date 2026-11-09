namespace SystematiaBot.Core.Entities;

/// <summary>Token usage of one LLM call, used for the daily budget and /status.</summary>
public class LlmUsage
{
    public int Id { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset At { get; set; }
    public required string Operation { get; set; }
    public string? Model { get; set; }
    public int InputTokens { get; set; }

    /// <summary>Part of InputTokens served from the provider's prompt cache (billed at a fraction of the price).</summary>
    public int CachedTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>Part of OutputTokens the model spent thinking before the visible answer.</summary>
    public int ReasoningTokens { get; set; }
    public double CostUsd { get; set; }
}
