using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

/// <param name="Operation">Short name for usage accounting ("score", "vacancy_draft", …).</param>
/// <param name="Json">Ask the model for a single JSON object.</param>
/// <param name="Cheap">High-volume task (scoring): use the cheaper model when one is configured.</param>
public sealed record LlmRequest(string Operation, string System, string User, bool Json = true, int? MaxOutputTokens = null, bool Cheap = false);

public sealed record LlmResponse(string Text, int InputTokens, int OutputTokens, int CachedTokens = 0, int ReasoningTokens = 0, string? Model = null);

/// <summary>Token and cost totals of one model over a period.</summary>
public sealed record LlmUsageSummary(string Model, int Calls, long InputTokens, long CachedTokens, long OutputTokens, long ReasoningTokens, double CostUsd);

/// <summary>Provider-agnostic chat completion. Core never references a concrete SDK.</summary>
public interface ILlmClient
{
    bool IsConfigured { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed class LlmUnavailableException(string message) : Exception(message);

public interface ILlmUsageRepository
{
    Task AddAsync(LlmUsage usage, CancellationToken ct);
    Task<double> GetCostAsync(DateOnly day, CancellationToken ct);

    /// <summary>Per-model totals from <paramref name="fromDay"/> (inclusive) to today.</summary>
    Task<List<LlmUsageSummary>> GetSummaryAsync(DateOnly fromDay, CancellationToken ct);
}

/// <summary>Prompt templates from prompts/*.md with {{placeholders}}.</summary>
public interface IPromptStore
{
    string Render(string name, IReadOnlyDictionary<string, string> values);
}
