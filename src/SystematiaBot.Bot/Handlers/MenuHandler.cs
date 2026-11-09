using System.Text;
using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
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
            : "⚠️ Gmail не підключено — бот не бачить листів Djinni");
        sb.AppendLine(string.IsNullOrWhiteSpace(settings.Profile)
            ? "⚠️ Профіль порожній — вакансії не оцінюються. Натисни «👤 Профіль»."
            : $"👤 Профіль: {Plural(settings.Profile.Length, "символ", "символи", "символів")}");
        sb.Append("\n💡 Надішли посилання на вакансію djinni.co/jobs/…, і я її оціню та напишу відгук.");

        await ui.ShowAsync(ctx, sb.ToString(), MainMenu(Count(VacancyStatus.New), Count(VacancyStatus.Replied), settings.DigestEnabled), ct);
    }

    public async Task<string?> StartSyncAsync(ChatContext ctx, CancellationToken ct)
    {
        if (jobs.IsRunning(ctx.ChatId, "sync"))
            return "Вже перевіряю пошту…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Перевіряю пошту…", ct);
        jobs.TryStart(ctx.ChatId, "sync", async jobCt =>
        {
            var outcome = await coordinator.RunAsync(ctx.ChatId, jobCt);
            var text = outcome switch
            {
                { Skipped: true } => "⏳ Пошта вже перевіряється за розкладом, нові картки прийдуть окремими повідомленнями.",
                { Error: { } error } => $"⚠️ <b>Не вдалося перевірити пошту</b>\n{Escape(error)}",
                { Result: { } r } when r.MailsRead == 0 => "✅ Нових листів від Djinni немає.",
                { Result: { } r } => $"✅ Прочитано листів: {r.MailsRead}\n🆕 Нових вакансій: {r.NewVacancies} · 📩 Відповідей: {r.NewEvents}\nКартки — вище.",
                _ => "✅ Готово."
            };
            await progress.CompleteAsync(text, Rows(BackToMenu), jobCt);
        });
        return null;
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

        if (openAiOptions.Value.IsConfigured)
        {
            var spent = await llm.GetSpentAsync(llm.Today(), ct);
            sb.AppendLine($"OpenAI сьогодні: ${spent:0.000} з ${openAiOptions.Value.DailyBudgetUsd:0.00} ({Escape(openAiOptions.Value.Model)})");
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
}
