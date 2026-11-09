using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.Routing;
using SystematiaBot.Bot.UI;
using SystematiaBot.Core.Abstractions;
using SystematiaBot.Core.Entities;
using SystematiaBot.Core.Jobs;
using Telegram.Bot.Types.ReplyMarkups;
using static SystematiaBot.Bot.UI.Keyboards;
using static SystematiaBot.Bot.UI.MessageFormatter;

namespace SystematiaBot.Bot.Handlers;

public sealed class VacancyHandler(
    BotUi ui,
    BackgroundJobRunner jobs,
    IServiceScopeFactory scopeFactory,
    IVacancyRepository vacancies,
    IUserSettingsRepository settingsRepository,
    IClock clock) : ICallbackHandler
{
    private const int PageSize = 8;

    public string Module => "v";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        if (data.Action == "list")
        {
            await ShowListAsync(ctx, data.Arg ?? "new", data.Id ?? 0, ct);
            return null;
        }

        if (data.Id is not { } id || await vacancies.GetAsync(id, ct) is not { } vacancy)
            return "Вакансію не знайдено";

        switch (data.Action)
        {
            case "show":
                await ui.ShowAsync(ctx, JobViews.Card(vacancy), JobViews.CardKeyboard(vacancy), ct);
                return null;
            case "draft" when vacancy.Draft is not null:
                await ui.ShowAsync(ctx, JobViews.DraftView(vacancy), JobViews.DraftKeyboard(vacancy), ct);
                return null;
            case "draft" or "redraft":
                return await StartDraftAsync(ctx, vacancy.Id, ct);
            case "score":
                return await StartRescoreAsync(ctx, vacancy.Id, ct);
            case "st" when JobViews.ParseStatus(data.Arg) is { } status:
                vacancy.Status = status;
                vacancy.UpdatedAt = clock.UtcNow;
                if (status == VacancyStatus.Applied)
                    vacancy.AppliedAt ??= clock.UtcNow;
                await vacancies.SaveChangesAsync(ct);
                await ui.ShowAsync(ctx, JobViews.Card(vacancy), JobViews.CardKeyboard(vacancy), ct);
                return "Статус: " + JobViews.StatusLabel(status);
            default:
                return null;
        }
    }

    /// <summary>The owner pasted a djinni.co/jobs/… link.</summary>
    public async Task AddFromUrlAsync(ChatContext ctx, string url, CancellationToken ct)
    {
        if (jobs.IsRunning(ctx.ChatId, "add"))
        {
            await ui.SendAsync(ctx, "⏳ Ще обробляю попереднє посилання, зачекай кілька секунд.", null, ct);
            return;
        }

        var progress = await ui.StartProgressAsync(ctx, "⏳ Читаю вакансію і оцінюю збіг з профілем…", ct);
        jobs.TryStart(ctx.ChatId, "add", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var profile = (await sp.GetRequiredService<IUserSettingsRepository>().GetOrCreateAsync(ctx.ChatId, jobCt)).Profile;
            var vacancy = await sp.GetRequiredService<MailSyncService>().AddFromUrlAsync(url, profile, jobCt);
            if (vacancy is null)
            {
                await progress.CompleteAsync("Не схоже на посилання на вакансію Djinni (djinni.co/jobs/…).", Rows(BackToMenu), jobCt);
                return;
            }

            var hint = string.IsNullOrWhiteSpace(profile) ? "\n\n⚠️ Профіль порожній, тому без оцінки. Заповни «👤 Профіль»." : "";
            await progress.CompleteAsync(JobViews.Card(vacancy) + hint, JobViews.CardKeyboard(vacancy), jobCt);
        });
    }

    private async Task ShowListAsync(ChatContext ctx, string list, int page, CancellationToken ct)
    {
        var statuses = JobViews.ListStatuses(list);
        var total = await vacancies.CountAsync(statuses, ct);
        var pages = Math.Max(1, (total + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 0, pages - 1);
        var items = await vacancies.GetByStatusAsync(statuses, page * PageSize, PageSize, ct);

        var rows = new List<InlineKeyboardButton[]>();
        rows.AddRange(items.Select(v => new[] { Btn(JobViews.ListItem(v), Cb.Vacancy(v.Id)) }));

        var nav = new List<InlineKeyboardButton>();
        if (page > 0)
            nav.Add(Btn("◀️", Cb.VacancyList(list, page - 1)));
        if (page < pages - 1)
            nav.Add(Btn("▶️", Cb.VacancyList(list, page + 1)));
        rows.Add([.. nav]);
        rows.Add(BackToMenu);

        var text = total == 0
            ? $"{JobViews.ListTitle(list)}\n\nПоки порожньо."
            : $"{JobViews.ListTitle(list)}: {total}" + (pages > 1 ? $" · сторінка {page + 1}/{pages}" : "");
        await ui.ShowAsync(ctx, text, Rows(rows), ct);
    }

    private async Task<string?> StartDraftAsync(ChatContext ctx, int vacancyId, CancellationToken ct)
    {
        var profile = (await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct)).Profile;
        if (string.IsNullOrWhiteSpace(profile))
            return "Спершу заповни профіль: «👤 Профіль»";
        if (jobs.IsRunning(ctx.ChatId, "draft"))
            return "Вже пишу відгук…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Пишу відгук під цю вакансію…", ct);
        jobs.TryStart(ctx.ChatId, "draft", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IVacancyRepository>();
            var vacancy = await repo.GetAsync(vacancyId, jobCt);
            if (vacancy is null)
                return;

            Draft? draft;
            try
            {
                draft = await sp.GetRequiredService<DraftWriter>().WriteApplicationAsync(profile, vacancy, jobCt);
            }
            catch (LlmUnavailableException ex)
            {
                await progress.CompleteAsync($"⚠️ {Escape(ex.Message)}", Rows([Btn("⬅️ До вакансії", Cb.Vacancy(vacancyId))]), jobCt);
                return;
            }

            if (draft is null)
            {
                await progress.CompleteAsync("😕 Модель не повернула текст. Спробуй ще раз.", Rows([Btn("🔄 Ще раз", Cb.Redraft(vacancyId))]), jobCt);
                return;
            }

            vacancy.Draft = draft.Text;
            vacancy.DraftWarnings = string.Join('\n', draft.UngroundedSkills);
            if (vacancy.Status == VacancyStatus.New)
                vacancy.Status = VacancyStatus.Drafted;
            vacancy.UpdatedAt = sp.GetRequiredService<IClock>().UtcNow;
            await repo.SaveChangesAsync(jobCt);
            await progress.CompleteAsync(JobViews.DraftView(vacancy), JobViews.DraftKeyboard(vacancy), jobCt);
        });
        return null;
    }

    private async Task<string?> StartRescoreAsync(ChatContext ctx, int vacancyId, CancellationToken ct)
    {
        var profile = (await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct)).Profile;
        if (string.IsNullOrWhiteSpace(profile))
            return "Спершу заповни профіль: «👤 Профіль»";
        if (jobs.IsRunning(ctx.ChatId, "score"))
            return "Вже оцінюю…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Оцінюю вакансію…", ct);
        jobs.TryStart(ctx.ChatId, "score", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var vacancy = await sp.GetRequiredService<IVacancyRepository>().GetAsync(vacancyId, jobCt);
            if (vacancy is null)
                return;

            await sp.GetRequiredService<MailSyncService>().EnrichAndScoreAsync(vacancy, profile, jobCt);
            await progress.CompleteAsync(JobViews.Card(vacancy), JobViews.CardKeyboard(vacancy), jobCt);
        });
        return null;
    }
}
