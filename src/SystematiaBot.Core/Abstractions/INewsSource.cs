using SystematiaBot.Core.Entities;

namespace SystematiaBot.Core.Abstractions;

/// <summary>A post or article as fetched from a source, before it becomes a <see cref="NewsItem"/>.</summary>
public sealed record FetchedNews(string Url, string? Title, string Text, DateTimeOffset PublishedAt);

public interface INewsSource
{
    SourceKind Kind { get; }

    /// <summary>Returns items published at or after <paramref name="since"/> (best effort, sources may return slightly older ones).</summary>
    Task<IReadOnlyList<FetchedNews>> FetchAsync(Source source, DateTimeOffset since, CancellationToken ct);
}

public interface INewsSourceFactory
{
    INewsSource For(SourceKind kind);
}
