using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;

namespace SystematiaBot.Core.Abstractions;

public sealed record NewsCounts(int New, int Weak, int Picked, int Rejected, int ReadyPosts);

public interface ISourceRepository
{
    /// <summary>Upserts the city's sources from config, disables removed ones and returns the enabled sources.</summary>
    Task<IReadOnlyList<Source>> SyncAsync(CityConfig city, CancellationToken ct);

    Task<IReadOnlyList<Source>> GetAllAsync(CancellationToken ct);
}

public interface INewsRepository
{
    Task<HashSet<(int SourceId, string Url)>> GetExistingKeysAsync(IReadOnlyCollection<int> sourceIds, CancellationToken ct);

    void Add(NewsItem item);

    /// <summary>New and picked items of the city published since the date, including their source.</summary>
    Task<List<NewsItem>> GetActiveAsync(string city, DateTimeOffset since, CancellationToken ct);

    Task<List<NewsItem>> GetByStatusAsync(string city, NewsStatus status, CancellationToken ct);

    Task<NewsItem?> GetAsync(int id, CancellationToken ct);

    /// <summary>Other items of the cluster (duplicates from other sources) with their sources.</summary>
    Task<List<NewsItem>> GetClusterMembersAsync(NewsItem primary, CancellationToken ct);

    Task<Dictionary<int, int>> GetClusterSizesAsync(IReadOnlyCollection<int> primaryIds, CancellationToken ct);

    Task<NewsCounts> GetCountsAsync(string city, DateTimeOffset since, int minScore, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
