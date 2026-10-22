using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;

namespace SystematiaBot.Infrastructure.Sources;

/// <summary>Leaves items unscored (used when no LLM is configured).</summary>
public sealed class NullNewsScorer : INewsScorer
{
    public Task ScoreAsync(CityConfig city, IReadOnlyList<NewsItem> items, CancellationToken ct) => Task.CompletedTask;
}
