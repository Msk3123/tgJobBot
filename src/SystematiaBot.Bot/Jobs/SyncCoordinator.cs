using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Jobs;

namespace SystematiaBot.Bot.Jobs;

public sealed record SyncOutcome(SyncResult? Result, int CardsSent, string? Error, bool Skipped);

/// <summary>
/// One mail sync at a time, whether it was started by the timer or by the "🔄" button:
/// two concurrent syncs would race on the same Gmail ids and the unique index would reject one of them halfway.
/// </summary>
public sealed class SyncCoordinator(IServiceScopeFactory scopeFactory, JobNotifier notifier, IClock clock, ErrorTracker errors, ILogger<SyncCoordinator> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SyncOutcome> RunAsync(long ownerChatId, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
            return new SyncOutcome(null, 0, null, Skipped: true);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var sync = sp.GetRequiredService<MailSyncService>();
            if (!sync.IsConfigured)
                return new SyncOutcome(null, 0, "Gmail не підключено: задай Gmail__ClientId, Gmail__ClientSecret, Gmail__RefreshToken.", false);

            var settingsRepository = sp.GetRequiredService<IUserSettingsRepository>();
            var settings = await settingsRepository.GetOrCreateAsync(ownerChatId, ct);

            var result = await sync.SyncAsync(settings.Profile, ct);
            settings.LastMailSyncAt = clock.UtcNow;
            await settingsRepository.SaveAsync(settings, ct);

            var sent = await notifier.SendPendingAsync(ownerChatId, sp, ct);
            return new SyncOutcome(result, sent, null, false);
        }
        catch (MailboxException ex)
        {
            logger.LogWarning("Mail sync failed: {Error}", ex.Message);
            errors.Record("пошта", ex);
            return new SyncOutcome(null, 0, ex.Message, false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
