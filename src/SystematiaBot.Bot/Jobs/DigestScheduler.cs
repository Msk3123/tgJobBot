using System.Globalization;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.News;
using SystematiaBot.Infrastructure.Sources;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Jobs;

/// <summary>Every day at Bot:DigestTime (Europe/Kyiv) collects news for subscribers and sends "Знайдено N нових кандидатів".</summary>
public sealed class DigestScheduler(
    IServiceScopeFactory scopeFactory,
    BotUi ui,
    ICityCatalog cities,
    IClock clock,
    ErrorTracker errors,
    IOptions<BotOptions> options,
    ILogger<DigestScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Digest tick failed");
                errors.Record("digest", ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = KyivTime.ToKyiv(clock.UtcNow);
        if (now.TimeOfDay < DigestTime(options.Value.DigestTime))
            return;

        var today = DateOnly.FromDateTime(now.DateTime);
        await using var scope = scopeFactory.CreateAsyncScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<IUserSettingsRepository>();
        foreach (var settings in await settingsRepository.GetDigestSubscribersAsync(ct))
        {
            if (settings.LastDigestDate == today)
                continue;

            // Mark first so a failure never turns into a message every minute.
            settings.LastDigestDate = today;
            await settingsRepository.SaveAsync(settings, ct);

            var ctx = new ChatContext { ChatId = settings.ChatId, UserId = settings.ChatId, IsOwner = settings.ChatId == options.Value.OwnerId };
            if (ctx.IsDemo)
            {
                await ui.SendAsync(ctx, "🗞 <b>Ранковий дайджест</b>\nЗнайдено 6 нових кандидатів (демо).", Rows([Btn("📋 Переглянути", new CallbackData("n", "col"))]), ct);
                continue;
            }

            var city = cities.Find(settings.City) ?? cities.Cities[0];
            logger.LogInformation("Digest for chat {ChatId}, city {City}", settings.ChatId, city.Id);
            var result = await scope.ServiceProvider.GetRequiredService<NewsCollectionService>().CollectAsync(city, null, ct);
            await ui.SendAsync(ctx, $"🗞 <b>Ранковий дайджест — {Escape(city.Name)}</b>\nЗнайдено {Plural(result.Added, "нового кандидата", "нових кандидати", "нових кандидатів")} · усього до розгляду: {result.Candidates}",
                Rows([Btn("📋 Переглянути", new CallbackData("n", "c", 0))]), ct);
        }
    }

    public static TimeSpan DigestTime(string value) =>
        TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time) ? time : new TimeSpan(9, 0, 0);

    /// <summary>Next run in Kyiv time, for the toggle confirmation.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset utcNow, string digestTime)
    {
        var now = KyivTime.ToKyiv(utcNow);
        var run = new DateTimeOffset(now.Date + DigestTime(digestTime), now.Offset);
        return run > now ? run : run.AddDays(1);
    }
}

/// <summary>Remembers the last unhandled error for /status.</summary>
public sealed class ErrorTracker
{
    public (DateTimeOffset At, string Where, string Message)? Last { get; private set; }

    public void Record(string where, Exception ex) => Last = (DateTimeOffset.UtcNow, where, ex.Message);
}
