using Microsoft.EntityFrameworkCore;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;

namespace SystematiaBot.Infrastructure.Persistence.Repositories;

public sealed class PostRepository(BotDbContext db) : IPostRepository
{
    public void Add(Post post) => db.Posts.Add(post);

    public void Remove(Post post) => db.Posts.Remove(post);

    public Task<Post?> GetAsync(int id, CancellationToken ct) =>
        db.Posts.Include(p => p.NewsItem).ThenInclude(n => n!.Source).FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Post?> GetLatestAsync(int newsItemId, PostKind kind, CancellationToken ct) =>
        db.Posts.Include(p => p.NewsItem).ThenInclude(n => n!.Source)
            .Where(p => p.NewsItemId == newsItemId && p.Kind == kind)
            .OrderByDescending(p => p.Version)
            .FirstOrDefaultAsync(ct);

    public async Task<int> GetNextVersionAsync(int newsItemId, PostKind kind, CancellationToken ct) =>
        (await db.Posts.Where(p => p.NewsItemId == newsItemId && p.Kind == kind).MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;

    public async Task<List<Post>> GetLatestByCityAsync(string city, PostKind kind, bool approvedOnly, CancellationToken ct)
    {
        var all = await db.Posts.Include(p => p.NewsItem).ThenInclude(n => n!.Source)
            .Where(p => p.Kind == kind && p.NewsItem!.City == city)
            .ToListAsync(ct);

        return all.GroupBy(p => p.NewsItemId)
            .Select(g => g.OrderByDescending(p => p.Version).First())
            .Where(p => !approvedOnly || p.IsApproved)
            .OrderBy(p => p.CreatedAt)
            .ToList();
    }

    public Task SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public sealed class LlmUsageRepository(BotDbContext db) : ILlmUsageRepository
{
    public async Task AddAsync(LlmUsage usage, CancellationToken ct)
    {
        db.LlmUsages.Add(usage);
        await db.SaveChangesAsync(ct);
    }

    public async Task<double> GetCostAsync(DateOnly day, CancellationToken ct) =>
        await db.LlmUsages.Where(u => u.Day == day).SumAsync(u => u.CostUsd, ct);
}
