using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Validation;

namespace SystematiaBot.Core.News;

public sealed record CollectProgress(string Stage, int SourcesDone, int SourcesTotal, int Posts, int ToScore = 0);

public sealed record CollectResult(int SourcesOk, int SourcesFailed, int Fetched, int Fresh, int NotLocal, int AlreadyKnown, int Added, int Duplicates, int Candidates, int Weak);

/// <summary>Scores news items for local interest (AI step). Implemented in Infrastructure.</summary>
public interface INewsScorer
{
    Task ScoreAsync(CityConfig city, IReadOnlyList<NewsItem> items, CancellationToken ct);
}

/// <summary>Collect → freshness → locality → store → dedup → score (SPEC §7.2).</summary>
public sealed class NewsCollectionService(
    ISourceRepository sources,
    INewsRepository news,
    INewsSourceFactory sourceFactory,
    INewsScorer scorer,
    DedupService dedup,
    IClock clock,
    IOptions<NewsOptions> options,
    ILogger<NewsCollectionService> logger)
{
    private readonly NewsOptions _options = options.Value;

    public async Task<CollectResult> CollectAsync(CityConfig city, IProgress<CollectProgress>? progress, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-_options.MaxAgeDays);
        var enabled = await sources.SyncAsync(city, ct);
        progress?.Report(new CollectProgress("fetch", 0, enabled.Count, 0));

        var fetched = new ConcurrentBag<(Source Source, FetchedNews Item)>();
        int done = 0, failed = 0;
        await Parallel.ForEachAsync(enabled, new ParallelOptions { MaxDegreeOfParallelism = _options.SourceConcurrency, CancellationToken = ct }, async (source, token) =>
        {
            try
            {
                var items = await sourceFactory.For(source.Kind).FetchAsync(source, since, token);
                foreach (var item in items)
                    fetched.Add((source, item));
                source.LastError = null;
                logger.LogInformation("Source {Source}: {Count} items", source.Name, items.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            {
                Interlocked.Increment(ref failed);
                source.LastError = $"{now:dd.MM HH:mm}: {ex.Message}";
                logger.LogWarning("Source {Source} failed: {Error}", source.Name, ex.Message);
            }

            source.LastFetchedAt = now;
            progress?.Report(new CollectProgress("fetch", Interlocked.Increment(ref done), enabled.Count, fetched.Count));
        });

        progress?.Report(new CollectProgress("filter", enabled.Count, enabled.Count, fetched.Count));
        // Future dates are site bugs; one-line service posts ("Повітряна тривога") are not news.
        var fresh = fetched.Where(f => f.Item.PublishedAt >= since && f.Item.PublishedAt <= now.AddHours(1)
                                       && TextValidator.CountWords(f.Item.Text) >= _options.MinWords).ToList();
        var local = fresh.Where(f => f.Source.IsCityOnly || LocalityFilter.MentionsCity(f.Item.Title + "\n" + f.Item.Text, city)).ToList();
        var known = await news.GetExistingKeysAsync(enabled.Select(s => s.Id).ToList(), ct);

        var added = new List<NewsItem>();
        foreach (var (source, item) in local)
        {
            if (!known.Add((source.Id, item.Url)))
                continue;

            var entity = new NewsItem
            {
                City = city.Id,
                SourceId = source.Id,
                Url = item.Url,
                Title = item.Title,
                RawText = item.Text.Trim(),
                PublishedAt = item.PublishedAt.ToUniversalTime(),
                FetchedAt = now,
                ContentHash = DedupService.ContentHash(item.Text)
            };
            news.Add(entity);
            added.Add(entity);
        }

        await news.SaveChangesAsync(ct);

        var active = await news.GetActiveAsync(city.Id, since, ct);
        var duplicates = MarkDuplicates(active);
        await news.SaveChangesAsync(ct);

        var toScore = active.Where(n => n.Status == NewsStatus.New && n.Score is null).ToList();
        if (toScore.Count > 0)
        {
            progress?.Report(new CollectProgress("score", enabled.Count, enabled.Count, fetched.Count, toScore.Count));
            await scorer.ScoreAsync(city, toScore, ct);
            await news.SaveChangesAsync(ct);
        }

        var candidates = active.Where(n => n.Status == NewsStatus.New).ToList();
        var weak = candidates.Count(n => n.Score is { } s && s < _options.MinScore);

        var result = new CollectResult(enabled.Count - failed, failed, fetched.Count, fresh.Count, fresh.Count - local.Count,
            local.Count - added.Count, added.Count, duplicates, candidates.Count - weak, weak);
        logger.LogInformation("Collect {City}: {@Result}", city.Id, result);
        return result;
    }

    /// <summary>Clusters active items; keeps the fullest text (or the already picked item) and marks the rest as duplicates.</summary>
    private int MarkDuplicates(List<NewsItem> active)
    {
        var clusters = dedup.Cluster(active.Select(n => new DedupService.Text(n.Id, (n.Title + "\n" + n.RawText).Trim(), n.PublishedAt)).ToList());
        var byId = active.ToDictionary(n => n.Id);
        var marked = 0;

        foreach (var cluster in clusters)
        {
            var members = cluster.Select(id => byId[id]).ToList();
            var primary = members.FirstOrDefault(m => m.Status == NewsStatus.Picked)
                          ?? members.OrderByDescending(m => m.RawText.Length).First();

            primary.ClusterId = primary.Id;
            foreach (var member in members.Where(m => m != primary))
            {
                member.ClusterId = primary.Id;
                if (member.Status == NewsStatus.Picked)
                    continue;

                member.Status = NewsStatus.Rejected;
                member.RejectReason = RejectReason.Duplicate;
                marked++;
            }
        }

        return marked;
    }
}

public static class LocalityFilter
{
    /// <summary>True when the text mentions the city or one of its local terms (case-insensitive stem match).</summary>
    public static bool MentionsCity(string text, CityConfig city)
    {
        var normalized = text.Replace('’', '\'').Replace('ʼ', '\'');
        return city.LocalTerms.Concat(city.Aliases).Append(city.Name)
            .Any(term => normalized.Contains(term.Replace('’', '\'').Replace('ʼ', '\''), StringComparison.OrdinalIgnoreCase));
    }
}
