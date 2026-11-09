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
    IMailEventRepository mailEvents,
    IUserSettingsRepository settingsRepository,
    IClock clock) : ICallbackHandler
{
    private const int PageSize = 8;

    /// <summary>The inbox is ranked in memory by fit × freshness; anything beyond this is stale noise anyway.</summary>
    private const int MaxRankedInbox = 200;

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
            case "cv":
                return await StartCvAsync(ctx, vacancy, ct);
            case "fu" when vacancy.FollowUpDraft is not null:
                await ui.ShowAsync(ctx, JobViews.FollowUpView(vacancy), JobViews.FollowUpDraftKeyboard(vacancy, await mailEvents.GetInboxUrlAsync(vacancy.Id, ct)), ct);
                return null;
            case "fu" or "fu2":
                return await StartFollowUpAsync(ctx, vacancy.Id, ct);
            case "fud" or "fun":
                vacancy.FollowUpDoneAt = clock.UtcNow;
                vacancy.UpdatedAt = clock.UtcNow;
                await vacancies.SaveChangesAsync(ct);
                await ui.ShowAsync(ctx, JobViews.Card(vacancy), JobViews.CardKeyboard(vacancy), ct);
                return data.Action == "fud" ? "Записав: follow-up надіслано" : "Більше не нагадаю про цю вакансію";
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
        var items = list == "new"
            ? (await vacancies.GetByStatusAsync(statuses, 0, MaxRankedInbox, ct))
                .OrderByDescending(v => VacancyPriority.Score(v, clock.UtcNow) ?? -1)
                .ThenByDescending(v => v.FoundAt)
                .Skip(page * PageSize).Take(PageSize).ToList()
            : await vacancies.GetByStatusAsync(statuses, page * PageSize, PageSize, ct);

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

    /// <summary>
    /// Tailored CV: the model only picks and orders items from the owner's own CV (by id) and may rewrite the headline
    /// and summary; every bullet in the PDF is the owner's original text, so nothing can be invented.
    /// </summary>
    private async Task<string?> StartCvAsync(ChatContext ctx, Vacancy vacancy, CancellationToken ct)
    {
        var settings = await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct);
        if (string.IsNullOrWhiteSpace(settings.CvJson) || CvData.TryParse(settings.CvJson, out _) is not { } baseCv)
        {
            await ui.SendAsync(ctx, CvHandler.NoCvHint, Rows(BackToMenu), ct);
            return "Спершу завантаж CV";
        }

        if (jobs.IsRunning(ctx.ChatId, "cv"))
            return "Вже збираю CV…";

        var vacancyId = vacancy.Id;
        var profile = settings.Profile;
        var progress = await ui.StartProgressAsync(ctx, "⏳ Підбираю проєкти й навички під цю вакансію…", ct);
        jobs.TryStart(ctx.ChatId, "cv", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var current = await sp.GetRequiredService<IVacancyRepository>().GetAsync(vacancyId, jobCt);
            if (current is null)
                return;

            TailoredCv tailored;
            string? note = null;
            try
            {
                tailored = await sp.GetRequiredService<CvTailor>().TailorAsync(baseCv, profile, current, jobCt);
            }
            catch (LlmUnavailableException ex)
            {
                tailored = new TailoredCv(baseCv.Clone(), [], false);
                note = ex.Message;
            }

            var pdf = sp.GetRequiredService<ICvRenderer>().Render(tailored.Cv);
            var caption = new System.Text.StringBuilder($"📄 CV під <b>{Escape(current.Title)}</b>");
            if (!string.IsNullOrWhiteSpace(current.Company))
                caption.Append(" · ").Append(Escape(current.Company));
            caption.Append(tailored.UsedModel
                ? "\nПроєкти, пункти й навички переставлені під вакансію. Усі пункти — твій оригінальний текст."
                : "\nБазова версія без адаптації" + (note is null ? "." : $": {Escape(note)}"));
            if (tailored.UngroundedSkills.Count > 0)
                caption.Append("\n⚠️ Заголовок або summary згадували ").Append(Escape(string.Join(", ", tailored.UngroundedSkills))).Append(", тому залишив твої оригінальні.");

            await ui.SendDocumentAsync(ctx.ChatId, pdf, CvHandler.FileName(baseCv.Name, current.Company), caption.ToString(),
                Rows([Btn("🔄 Інший варіант", Cb.Cv(vacancyId)), Btn("⬅️ До вакансії", Cb.Vacancy(vacancyId))]), jobCt);
            await progress.CompleteAsync("✅ CV готове — файл нижче. Перевір перед відправкою.", null, jobCt);
        });
        return null;
    }

    private async Task<string?> StartFollowUpAsync(ChatContext ctx, int vacancyId, CancellationToken ct)
    {
        var profile = (await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct)).Profile;
        if (string.IsNullOrWhiteSpace(profile))
            return "Спершу заповни профіль: «👤 Профіль»";
        if (jobs.IsRunning(ctx.ChatId, "followup"))
            return "Вже пишу…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Пишу коротке нагадування рекрутеру…", ct);
        jobs.TryStart(ctx.ChatId, "followup", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IVacancyRepository>();
            var vacancy = await repo.GetAsync(vacancyId, jobCt);
            if (vacancy is null)
                return;

            var now = sp.GetRequiredService<IClock>().UtcNow;
            var days = Math.Max(1, (int)(now - (vacancy.AppliedAt ?? vacancy.UpdatedAt)).TotalDays);
            Draft? draft;
            try
            {
                draft = await sp.GetRequiredService<DraftWriter>().WriteFollowUpAsync(profile, vacancy, days, jobCt);
            }
            catch (LlmUnavailableException ex)
            {
                await progress.CompleteAsync($"⚠️ {Escape(ex.Message)}", Rows([Btn("⬅️ До вакансії", Cb.Vacancy(vacancyId))]), jobCt);
                return;
            }

            if (draft is null)
            {
                await progress.CompleteAsync("😕 Модель не повернула текст. Спробуй ще раз.", Rows([Btn("🔄 Ще раз", Cb.FollowUpAgain(vacancyId))]), jobCt);
                return;
            }

            vacancy.FollowUpDraft = draft.Text;
            vacancy.FollowUpRemindedAt ??= now;
            vacancy.UpdatedAt = now;
            await repo.SaveChangesAsync(jobCt);
            var inbox = await sp.GetRequiredService<IMailEventRepository>().GetInboxUrlAsync(vacancyId, jobCt);
            await progress.CompleteAsync(JobViews.FollowUpView(vacancy), JobViews.FollowUpDraftKeyboard(vacancy, inbox), jobCt);
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
