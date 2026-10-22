using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

/// <param name="Operation">Short name for usage accounting ("score", "rewrite-social", …).</param>
/// <param name="Json">Ask the model for a single JSON object.</param>
public sealed record LlmRequest(string Operation, string System, string User, bool Json = true, int? MaxOutputTokens = null);

public sealed record LlmResponse(string Text, int InputTokens, int OutputTokens);

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
}

/// <summary>Prompt templates from prompts/*.md with {{placeholders}}.</summary>
public interface IPromptStore
{
    string Render(string name, IReadOnlyDictionary<string, string> values);
}

public interface IPostRepository
{
    void Add(Post post);
    void Remove(Post post);
    Task<Post?> GetAsync(int id, CancellationToken ct);
    Task<Post?> GetLatestAsync(int newsItemId, PostKind kind, CancellationToken ct);
    Task<int> GetNextVersionAsync(int newsItemId, PostKind kind, CancellationToken ct);

    /// <summary>Latest version of each post of the city (with its news item and source).</summary>
    Task<List<Post>> GetLatestByCityAsync(string city, PostKind kind, bool approvedOnly, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
