using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Bot.Jobs;

/// <param name="FeedVacancies">New vacancies from RSS boards; null when no feed was checked in this run.</param>
/// <param name="MailNotConfigured">Gmail is not connected yet; feeds still ran.</param>
public sealed record SyncOutcome(SyncResult? Mail, int? FeedVacancies, int CardsSent, string? Error, bool Skipped, bool MailNotConfigured);

/// <summary>
/// One sync (Gmail + RSS boards) at a time, whether it was started by the timer or by the "🔄" button:
/// two concurrent syncs would race on the same Gmail ids and the unique index would reject one of them halfway.
/// </summary>
public sealed class SyncCoordinator(IServiceScopeFactory scopeFactory, JobNotifier notifier, IClock clock, ErrorTracker errors, ILogger<SyncCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SyncOutcome> RunAsync(long ownerChatId, bool mail, bool feeds, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new SyncOutcome(null, null, 0, null, Skipped: true, false);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var settingsRepository = sp.GetRequiredService<IUserSettingsRepository>();
            var settings = await settingsRepository.GetOrCreateAsync(ownerChatId, ct);

            SyncResult? mailResult = null;
            string? error = null;
            var mailSync = sp.GetRequiredService<MailSyncService>();
            if (mail && mailSync.IsConfigured)
            {
                try
                {
                    mailResult = await mailSync.SyncAsync(settings.Profile, ct);
                    settings.LastMailSyncAt = clock.UtcNow;
                }
                catch (MailboxException ex)
                {
                    logger.LogWarning("Mail sync failed: {Error}", ex.Message);
                    errors.Record("пошта", ex);
                    error = ex.Message;
                }
            }

            int? feedVacancies = null;
            var feedSync = sp.GetRequiredService<FeedSyncService>();
            if (feeds && feedSync.HasFeeds)
            {
                feedVacancies = await feedSync.SyncAsync(settings.Profile, ct);
                settings.LastFeedSyncAt = clock.UtcNow;
            }

            await settingsRepository.SaveAsync(settings, ct);
            var sent = await notifier.SendPendingAsync(ownerChatId, sp, ct);
            return new SyncOutcome(mailResult, feedVacancies, sent, error, false, mail && !mailSync.IsConfigured);
        }
        finally
        {
            _gate.Release();
        }
    }
}
