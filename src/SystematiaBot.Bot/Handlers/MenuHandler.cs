using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;
using SystematiaBot.Infrastructure.Llm;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

public sealed class MenuHandler(
    BotUi ui,
    UserStateStore state,
    BackgroundJobRunner jobs,
    SyncCoordinator coordinator,
    IUserSettingsRepository settingsRepository,
    IVacancyRepository vacancies,
    IMailbox mailbox,
    FeedSyncService feeds,
    ILlmUsageRepository usage,
    LlmBudgetGuard llm,
    IOptions<OpenAiOptions> openAiOptions,
    IOptions<BotOptions> botOptions,
    ErrorTracker errors) : ICallbackHandler
{
    public string Module => "m";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        switch (data.Action)
        {
            case "main":
                state.Reset(ctx.ChatId);
                await ShowMainAsync(ctx, ct);
                return null;
            case "status":
                await ShowStatusAsync(ctx, ct);
                return null;
            case "sync":
                return await StartSyncAsync(ctx, ct);
            case "digest":
                var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
                settings.DigestEnabled = !settings.DigestEnabled;
                await settingsRepository.SaveAsync(settings, ct);
                await ShowMainAsync(ctx, ct);
                return settings.DigestEnabled ? $"Дайджест щодня о {botOptions.Value.DigestTime}" : "Дайджест вимкнено";
            default:
                return null;
        }
    }

    public async Task ShowMainAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        var counts = await vacancies.GetCountsAsync(ct);
        int Count(VacancyStatus s) => counts.GetValueOrDefault(s);

        var sb = new StringBuilder("🎯 <b>Job Hunter</b>\n\n");
        sb.AppendLine($"🆕 Нові: {Count(VacancyStatus.New)} · ✍️ Чернетки: {Count(VacancyStatus.Drafted)} · 📤 Подано: {Count(VacancyStatus.Applied)}");
        sb.AppendLine($"📩 Відповіді: {Count(VacancyStatus.Replied)} · 🗓 Співбесіди: {Count(VacancyStatus.Interview)} · ❌ Відмови: {Count(VacancyStatus.Rejected)}");
        sb.AppendLine();
        sb.AppendLine(mailbox.IsConfigured
            ? $"📬 Пошта: {(settings.LastMailSyncAt is { } at ? "перевірено " + JobViews.Date(at) : "ще не перевіряли")}"
            : "⚠️ Gmail не підключено — бот не бачить листів Djinni і LinkedIn");
        if (feeds.HasFeeds)
            sb.AppendLine($"🌐 DOU: {(settings.LastFeedSyncAt is { } feedAt ? "перевірено " + JobViews.Date(feedAt) : "ще не перевіряли")}");
        sb.AppendLine(string.IsNullOrWhiteSpace(settings.Profile)
            ? "⚠️ Профіль порожній — вакансії не оцінюються. Натисни «👤 Профіль»."
            : $"👤 Профіль: {Plural(settings.Profile.Length, "символ", "символи", "символів")}");
        sb.Append("\n💡 Надішли посилання на вакансію djinni.co/jobs/…, і я її оціню та напишу відгук.");

        await ui.ShowAsync(ctx, sb.ToString(), MainMenu(Count(VacancyStatus.New), Count(VacancyStatus.Replied), settings.DigestEnabled), ct);
    }

    public async Task<string?> StartSyncAsync(ChatContext ctx, CancellationToken ct)
    {
        if (jobs.IsRunning(ctx.ChatId, "sync"))
            return "Вже перевіряю…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Перевіряю пошту і DOU…", ct);
        jobs.TryStart(ctx.ChatId, "sync", async jobCt =>
        {
            var outcome = await coordinator.RunAsync(ctx.ChatId, mail: true, feeds: true, jobCt);
            await progress.CompleteAsync(SyncText(outcome), Rows(BackToMenu), jobCt);
        });
        return null;
    }

    private static string SyncText(SyncOutcome outcome)
    {
        if (outcome.Skipped)
            return "⏳ Перевірка вже йде за розкладом, нові картки прийдуть окремими повідомленнями.";

        var sb = new StringBuilder();
        if (outcome.Error is { } error)
            sb.AppendLine($"⚠️ <b>Не вдалося перевірити пошту</b>\n{Escape(error)}");
        else if (outcome.MailNotConfigured)
            sb.AppendLine("📭 Gmail не підключено — листи Djinni і LinkedIn не читаю.");
        else if (outcome.Mail is { } mail)
            sb.AppendLine(mail.MailsRead == 0
                ? "📬 Нових листів немає."
                : $"📬 Листів: {mail.MailsRead} · 🆕 вакансій: {mail.NewVacancies} · 📩 відповідей: {mail.NewEvents}");

        if (outcome.FeedVacancies is { } fromFeeds)
            sb.AppendLine($"🌐 DOU: нових вакансій {fromFeeds}");

        sb.Append(outcome.CardsSent > 0
            ? $"\n✅ Карток надіслано: {outcome.CardsSent} — вони вище."
            : "\n✅ Нових карток немає. Слабкі збіги й відфільтровані — у «🆕 Нові» та архіві.");
        return sb.ToString();
    }

    public async Task ShowStatusAsync(ChatContext ctx, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        var uptime = DateTimeOffset.UtcNow - BuildInfo.StartedAt;
        var sb = new StringBuilder("ℹ️ <b>Статус</b>\n");
        sb.AppendLine($"Версія: <code>{Escape(BuildInfo.ShortVersion)}</code>");
        sb.AppendLine($"Аптайм: {(int)uptime.TotalDays} д {uptime.Hours} год {uptime.Minutes} хв");
        sb.AppendLine($"Фонових задач: {jobs.RunningCount}");
        sb.AppendLine($"Gmail: {(mailbox.IsConfigured ? "підключено" : "не підключено")} · остання перевірка: {(settings.LastMailSyncAt is { } at ? JobViews.Date(at) : "—")}");
        sb.AppendLine(feeds.HasFeeds
            ? $"DOU RSS: остання перевірка {(settings.LastFeedSyncAt is { } feedAt ? JobViews.Date(feedAt) : "—")}"
            : "DOU RSS: вимкнено (Jobs:DouFeeds)");
        sb.AppendLine($"CV: {(string.IsNullOrWhiteSpace(settings.CvJson) ? "не завантажено (/cv)" : "є")}");

        if (openAiOptions.Value.IsConfigured)
        {
            var o = openAiOptions.Value;
            var today = llm.Today();
            var spent = await llm.GetSpentAsync(today, ct);
            sb.AppendLine();
            sb.AppendLine($"🤖 <b>OpenAI</b> · оцінка: {Escape(o.CheapModel ?? o.Model)} · тексти: {Escape(o.Model)}");
            sb.AppendLine($"Сьогодні: ${spent:0.000} з ${o.DailyBudgetUsd:0.00}");
            foreach (var row in await usage.GetSummaryAsync(today, ct))
                sb.AppendLine("  " + UsageLine(row));

            var month = await usage.GetSummaryAsync(new DateOnly(today.Year, today.Month, 1), ct);
            if (month.Count > 0)
                sb.AppendLine($"За місяць: ${month.Sum(r => r.CostUsd):0.00} · {Plural(month.Sum(r => r.Calls), "виклик", "виклики", "викликів")}");
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("OpenAI: не налаштовано (OpenAI:ApiKey / OpenAI:Model)");
        }

        sb.Append(errors.Last is { } error
            ? $"Остання помилка: {JobViews.Date(error.At)} · {Escape(error.Where)} · {Escape(Truncate(error.Message, 160))}"
            : "Помилок з моменту запуску не було ✅");
        await ui.ShowAsync(ctx, sb.ToString(), Rows(BackToMenu), ct);
    }

    /// <summary>"gpt-6-luna: 23 виклики · in 41.2k (кеш 30.1k) · out 5.3k (reasoning 2.0k) · $0.004"</summary>
    private static string UsageLine(LlmUsageSummary row)
    {
        var line = new StringBuilder($"{Escape(row.Model)}: {Plural(row.Calls, "виклик", "виклики", "викликів")} · in {Tokens(row.InputTokens)}");
        if (row.CachedTokens > 0)
            line.Append($" (кеш {Tokens(row.CachedTokens)})");
        line.Append($" · out {Tokens(row.OutputTokens)}");
        if (row.ReasoningTokens > 0)
            line.Append($" (reasoning {Tokens(row.ReasoningTokens)})");
        return line.Append($" · ${row.CostUsd:0.000}").ToString();
    }

    private static string Tokens(long n) => n >= 1000 ? $"{n / 1000.0:0.0}k" : n.ToString();
}
