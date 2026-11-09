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

/// <summary>Recruiter messages and rejections that arrived through Djinni.</summary>
public sealed class ConversationHandler(
    BotUi ui,
    BackgroundJobRunner jobs,
    IServiceScopeFactory scopeFactory,
    IMailEventRepository mails,
    IUserSettingsRepository settingsRepository) : ICallbackHandler
{
    private const int PageSize = 8;

    public string Module => "c";

    public async Task<string?> HandleAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        if (data.Action == "list")
        {
            await ShowListAsync(ctx, data.Id ?? 0, ct);
            return null;
        }

        if (data.Id is not { } id || await mails.GetAsync(id, ct) is not { } mailEvent)
            return "Повідомлення не знайдено";

        switch (data.Action)
        {
            case "show":
                await ui.ShowAsync(ctx, JobViews.EventCard(mailEvent), JobViews.EventKeyboard(mailEvent), ct);
                return null;
            case "reply" when mailEvent.ReplyDraft is not null:
                await ui.ShowAsync(ctx, JobViews.ReplyView(mailEvent), JobViews.ReplyKeyboard(mailEvent), ct);
                return null;
            case "reply" or "rereply":
                return await StartReplyAsync(ctx, mailEvent.Id, ct);
            default:
                return null;
        }
    }

    private async Task ShowListAsync(ChatContext ctx, int page, CancellationToken ct)
    {
        page = Math.Max(0, page);
        var items = await mails.GetConversationsAsync(page * PageSize, PageSize + 1, ct);
        var hasNext = items.Count > PageSize;

        var rows = new List<InlineKeyboardButton[]>();
        rows.AddRange(items.Take(PageSize).Select(e => new[] { Btn(JobViews.EventListItem(e), Cb.Conversation(e.Id)) }));
        var nav = new List<InlineKeyboardButton>();
        if (page > 0)
            nav.Add(Btn("◀️", Cb.Conversations(page - 1)));
        if (hasNext)
            nav.Add(Btn("▶️", Cb.Conversations(page + 1)));
        rows.Add([.. nav]);
        rows.Add(BackToMenu);

        var text = items.Count == 0 ? "📩 <b>Відповіді рекрутерів</b>\n\nПоки нічого." : "📩 <b>Відповіді рекрутерів</b>";
        await ui.ShowAsync(ctx, text, Rows(rows), ct);
    }

    private async Task<string?> StartReplyAsync(ChatContext ctx, int eventId, CancellationToken ct)
    {
        var profile = (await settingsRepository.GetOrCreateAsync(ctx.ChatId, ct)).Profile;
        if (string.IsNullOrWhiteSpace(profile))
            return "Спершу заповни профіль: «👤 Профіль»";
        if (jobs.IsRunning(ctx.ChatId, "reply"))
            return "Вже пишу відповідь…";

        var progress = await ui.StartProgressAsync(ctx, "⏳ Пишу відповідь рекрутеру…", ct);
        jobs.TryStart(ctx.ChatId, "reply", async jobCt =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IMailEventRepository>();
            var mailEvent = await repo.GetAsync(eventId, jobCt);
            if (mailEvent is null)
                return;

            Draft? draft;
            try
            {
                draft = await sp.GetRequiredService<DraftWriter>().WriteReplyAsync(profile, mailEvent, jobCt);
            }
            catch (LlmUnavailableException ex)
            {
                await progress.CompleteAsync($"⚠️ {Escape(ex.Message)}", Rows([Btn("⬅️ Назад", Cb.Conversation(eventId))]), jobCt);
                return;
            }

            if (draft is null)
            {
                await progress.CompleteAsync("😕 Модель не повернула текст. Спробуй ще раз.", Rows([Btn("🔄 Ще раз", Cb.Rereply(eventId))]), jobCt);
                return;
            }

            mailEvent.ReplyDraft = draft.Text;
            await repo.SaveChangesAsync(jobCt);
            var warning = draft.UngroundedSkills.Count > 0
                ? $"\n\n⚠️ У тексті є навички, яких немає в профілі: <b>{Escape(string.Join(", ", draft.UngroundedSkills))}</b>."
                : "";
            await progress.CompleteAsync(JobViews.ReplyView(mailEvent) + warning, JobViews.ReplyKeyboard(mailEvent), jobCt);
        });
        return null;
    }
}
