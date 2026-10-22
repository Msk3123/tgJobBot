namespace SystematiaBot.Core.Entities;

/// <summary>Token usage of one LLM call, used for the daily budget and /status.</summary>
public class LlmUsage
{
    public int Id { get; set; }
    public DateOnly Day { get; set; }
    public DateTimeOffset At { get; set; }
    public required string Operation { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public double CostUsd { get; set; }
}
