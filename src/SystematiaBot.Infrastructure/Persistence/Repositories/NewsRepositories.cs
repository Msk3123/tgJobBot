using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.News;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class SourceRepository(BotDbContext db) : ISourceRepository
{
    public async Task<IReadOnlyList<Source>> SyncAsync(CityConfig city, CancellationToken ct)
    {
        var existing = await db.Sources.Where(s => s.City == city.Id).ToListAsync(ct);
        var configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var config in city.Sources)
        {
            configured.Add(config.Url);
            var source = existing.FirstOrDefault(s => string.Equals(s.Url, config.Url, StringComparison.OrdinalIgnoreCase));
            if (source is null)
            {
                source = new Source { City = city.Id, Name = config.Name, Url = config.Url };
                db.Sources.Add(source);
            }

            source.Name = config.Name;
            source.Kind = config.Kind;
            source.IsCityOnly = config.IsCityOnly;
            source.IsEnabled = config.IsEnabled;
            source.Selectors = config.Selectors is null ? null : JsonSerializer.Serialize(config.Selectors);
        }

        foreach (var removed in existing.Where(s => !configured.Contains(s.Url)))
            removed.IsEnabled = false;

        await db.SaveChangesAsync(ct);
        return await db.Sources.Where(s => s.City == city.Id && s.IsEnabled).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Source>> GetAllAsync(CancellationToken ct) =>
        await db.Sources.OrderBy(s => s.City).ThenBy(s => s.Name).ToListAsync(ct);
}

public sealed class NewsRepository(BotDbContext db) : INewsRepository
{
    public async Task<HashSet<(int SourceId, string Url)>> GetExistingKeysAsync(IReadOnlyCollection<int> sourceIds, CancellationToken ct)
    {
        var keys = await db.NewsItems.Where(n => sourceIds.Contains(n.SourceId)).Select(n => new { n.SourceId, n.Url }).ToListAsync(ct);
        return keys.Select(k => (k.SourceId, k.Url)).ToHashSet();
    }

    public void Add(NewsItem item) => db.NewsItems.Add(item);

    public Task<List<NewsItem>> GetActiveAsync(string city, DateTimeOffset since, CancellationToken ct) =>
        db.NewsItems.Include(n => n.Source)
            .Where(n => n.City == city && n.PublishedAt >= since && (n.Status == NewsStatus.New || n.Status == NewsStatus.Picked))
            .ToListAsync(ct);

    public Task<List<NewsItem>> GetByStatusAsync(string city, NewsStatus status, CancellationToken ct) =>
        db.NewsItems.Include(n => n.Source)
            .Where(n => n.City == city && n.Status == status)
            .OrderByDescending(n => n.PublishedAt)
            .ToListAsync(ct);

    public Task<NewsItem?> GetAsync(int id, CancellationToken ct) =>
        db.NewsItems.Include(n => n.Source).FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<List<NewsItem>> GetClusterMembersAsync(NewsItem primary, CancellationToken ct) =>
        db.NewsItems.Include(n => n.Source)
            .Where(n => n.ClusterId == primary.Id && n.Id != primary.Id)
            .ToListAsync(ct);

    public async Task<Dictionary<int, int>> GetClusterSizesAsync(IReadOnlyCollection<int> primaryIds, CancellationToken ct)
    {
        var rows = await db.NewsItems
            .Where(n => n.ClusterId != null && primaryIds.Contains(n.ClusterId.Value) && n.Id != n.ClusterId)
            .GroupBy(n => n.ClusterId!.Value)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(r => r.Id, r => r.Count);
    }

    public async Task<NewsCounts> GetCountsAsync(string city, DateTimeOffset since, int minScore, CancellationToken ct)
    {
        var fresh = await db.NewsItems.Where(n => n.City == city && n.Status == NewsStatus.New && n.PublishedAt >= since)
            .Select(n => n.Score).ToListAsync(ct);
        var picked = await db.NewsItems.CountAsync(n => n.City == city && n.Status == NewsStatus.Picked, ct);
        var rejected = await db.NewsItems.CountAsync(n => n.City == city && n.Status == NewsStatus.Rejected
                                                          && n.RejectReason != RejectReason.Duplicate && n.RejectReason != RejectReason.NotLocal, ct);
        var ready = await db.Posts.CountAsync(p => p.Kind == PostKind.Social && p.IsApproved && p.NewsItem!.City == city, ct);
        var weak = fresh.Count(s => s is { } score && score < minScore);
        return new NewsCounts(fresh.Count - weak, weak, picked, rejected, ready);
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
