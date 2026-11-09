using Microsoft.Extensions.Logging;
using SystematiaBot.Core.Abstractions;

namespace SystematiaBot.Core.Jobs;

/// <summary>Pulls RSS-style boards (DOU) through the same intake as e-mail alerts.</summary>
public sealed class FeedSyncService(IEnumerable<IVacancyFeed> feeds, VacancyIntake intake, ILogger<FeedSyncService> logger)
{
    public bool HasFeeds => feeds.Any();

    public async Task<int> SyncAsync(string? profile, CancellationToken ct)
    {
        var created = 0;
        foreach (var feed in feeds)
        {
            IReadOnlyList<VacancyCandidate> items;
            try
            {
                items = await feed.FetchAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Feed {Source} failed: {Error}", feed.Source, ex.Message);
                continue;
            }

            foreach (var item in items.OrderBy(i => i.PostedAt))
            {
                if ((await intake.IngestAsync(item, profile, applyPrefilter: true, ct)).Created)
                    created++;
            }
        }

        if (created > 0)
            logger.LogInformation("Feed sync: {Count} new vacancies", created);
        return created;
    }
}
