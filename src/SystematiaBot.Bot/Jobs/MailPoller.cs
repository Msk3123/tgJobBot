using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;
using SystematiaBot.Infrastructure.Time;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Jobs;

/// <summary>Checks the mailbox every Jobs:PollMinutes and sends the morning digest at Bot:DigestTime (Europe/Kyiv).</summary>
public sealed class MailPoller(
    IServiceScopeFactory scopeFactory,
    SyncCoordinator coordinator,
    BotUi ui,
    IClock clock,
    ErrorTracker errors,
    IOptions<BotOptions> botOptions,
    IOptions<JobOptions> jobOptions,
    ILogger<MailPoller> logger) : BackgroundService
{
    private static readonly TimeSpan ErrorReminder = TimeSpan.FromHours(6);
    private DateTimeOffset _lastSync = DateTimeOffset.MinValue;
    private (string Message, DateTimeOffset At)? _lastReportedError;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var owner = botOptions.Value.OwnerId;
        if (owner == 0)
        {
            logger.LogWarning("Bot:OwnerId is not set: mail polling and digest are disabled");
            return;
        }

        // Let the bot connect to Telegram first.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await TickAsync(owner, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Mail poller tick failed");
                errors.Record("пошта/дайджест", ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(long owner, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (now - _lastSync >= TimeSpan.FromMinutes(Math.Max(1, jobOptions.Value.PollMinutes)))
        {
            _lastSync = now;
            var outcome = await coordinator.RunAsync(owner, ct);
            if (outcome.Error is { } error && ShouldReport(error, now))
                await ui.SendAsync(owner, $"⚠️ <b>Не вдалося перевірити пошту</b>\n{Escape(error)}", Rows([Btn("ℹ️ Статус", Cb.Status)]), ct);
            else if (outcome.Error is null && !outcome.Skipped)
                _lastReportedError = null;
        }

        await SendDigestIfDueAsync(owner, ct);
    }

    /// <summary>The same error at most once every 6 hours, so a revoked token does not mean a message every 5 minutes.</summary>
    private bool ShouldReport(string error, DateTimeOffset now)
    {
        if (_lastReportedError is { } last && last.Message == error && now - last.At < ErrorReminder)
            return false;
        _lastReportedError = (error, now);
        return true;
    }

    private async Task SendDigestIfDueAsync(long owner, CancellationToken ct)
    {
        var now = KyivTime.ToKyiv(clock.UtcNow);
        if (now.TimeOfDay < DigestTime(botOptions.Value.DigestTime))
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<IUserSettingsRepository>();
        var settings = await settingsRepository.GetOrCreateAsync(owner, ct);
        var today = DateOnly.FromDateTime(now.DateTime);
        if (!settings.DigestEnabled || settings.LastDigestDate == today)
            return;

        // Mark first so a failure never turns into a message every minute.
        settings.LastDigestDate = today;
        await settingsRepository.SaveAsync(settings, ct);

        var vacancies = scope.ServiceProvider.GetRequiredService<IVacancyRepository>();
        var counts = await vacancies.GetCountsAsync(ct);
        int Count(VacancyStatus s) => counts.GetValueOrDefault(s);

        var fresh = await vacancies.GetByStatusAsync(JobViews.NewList, 0, 3, ct);
        var sb = new StringBuilder("☀️ <b>Ранковий дайджест</b>\n\n");
        sb.AppendLine($"🆕 Нових вакансій: {Count(VacancyStatus.New)}");
        foreach (var vacancy in fresh.OrderByDescending(v => v.FitScore ?? -1))
            sb.AppendLine($"   • {Escape(JobViews.ListItem(vacancy))}");
        if (Count(VacancyStatus.Drafted) > 0)
            sb.AppendLine($"✍️ Чернетки, які ще не подані: {Count(VacancyStatus.Drafted)}");
        sb.AppendLine($"📤 Подано: {Count(VacancyStatus.Applied)} · 📩 Відповіді: {Count(VacancyStatus.Replied)} · 🗓 Співбесіди: {Count(VacancyStatus.Interview)}");
        if (string.IsNullOrWhiteSpace(settings.Profile))
            sb.AppendLine("\n⚠️ Профіль не заповнений, тому вакансії не оцінюються. Натисни «👤 Профіль».");

        await ui.SendAsync(owner, sb.ToString(), Rows(
            [Btn("🆕 Нові", Cb.VacancyList("new")), Btn("📋 Мої заявки", Cb.VacancyList("act"))]), ct);
    }

    public static TimeSpan DigestTime(string value) =>
        TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time) ? time : new TimeSpan(9, 0, 0);
}

/// <summary>Remembers the last unhandled error for /status.</summary>
public sealed class ErrorTracker
{
    public (DateTimeOffset At, string Where, string Message)? Last { get; private set; }

    public void Record(string where, Exception ex) => Last = (DateTimeOffset.UtcNow, where, ex.Message);
}
