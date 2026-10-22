using Microsoft.Extensions.Options;
using SystematiaBot.Bot.Demo;
using SystematiaBot.Bot.Handlers;
using SystematiaBot.Bot.Hosting;
using SystematiaBot.Bot.Jobs;
using SystematiaBot.Bot.State;
using SystematiaBot.Bot.UI;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace SystematiaBot.Bot.Routing;

/// <summary>Entry point for every update: access check, commands, awaited text input and callback dispatch.</summary>
public sealed class UpdateRouter(
    IEnumerable<ICallbackHandler> callbackHandlers,
    IEnumerable<ITextInputHandler> textHandlers,
    MenuHandler menu,
    SeoHandler seo,
    DemoHandler demo,
    UserStateStore state,
    BackgroundJobRunner jobs,
    BotUi ui,
    ErrorTracker errors,
    IOptions<BotOptions> options,
    ILogger<UpdateRouter> logger)
{
    public async Task RouteAsync(Update update, CancellationToken ct)
    {
        var ctx = CreateContext(update);
        if (ctx is null)
            return;

        try
        {
            switch (update.Type)
            {
                case UpdateType.Message when update.Message!.Text is { } text:
                    await OnTextAsync(ctx, text, ct);
                    break;
                case UpdateType.CallbackQuery:
                    await OnCallbackAsync(ctx, update.CallbackQuery!, ct);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Update {UpdateId} from chat {ChatId} failed", update.Id, ctx.ChatId);
            errors.Record("обробка повідомлення", ex);
            try
            {
                await ui.SendAsync(ctx.ChatId, "😕 Щось пішло не так, вже записав у лог.", Keyboards.Rows(Keyboards.BackToMenu), ct);
            }
            catch (Exception notifyEx)
            {
                logger.LogWarning(notifyEx, "Could not notify chat {ChatId} about the failure", ctx.ChatId);
            }
        }
    }

    private ChatContext? CreateContext(Update update)
    {
        var (chat, from, messageId) = update.Type switch
        {
            UpdateType.Message => (update.Message!.Chat, update.Message.From, (int?)null),
            UpdateType.CallbackQuery => (update.CallbackQuery!.Message?.Chat, update.CallbackQuery.From, update.CallbackQuery.Message?.MessageId),
            _ => (null, null, null)
        };

        if (chat is null || from is null)
            return null;

        return new ChatContext
        {
            ChatId = chat.Id,
            UserId = from.Id,
            Username = from.Username,
            IsOwner = options.Value.OwnerId != 0 && from.Id == options.Value.OwnerId,
            MessageId = messageId
        };
    }

    private async Task OnTextAsync(ChatContext ctx, string text, CancellationToken ct)
    {
        var command = text.StartsWith('/') ? text.Split(' ', '@')[0].ToLowerInvariant() : null;
        switch (command)
        {
            case "/start":
                logger.LogInformation("/start from chat {ChatId}, user {UserId} (@{Username}), owner: {IsOwner}", ctx.ChatId, ctx.UserId, ctx.Username, ctx.IsOwner);
                state.Reset(ctx.ChatId);
                await menu.ShowMainAsync(ctx, ct);
                return;
            case "/seo":
                await DispatchCallbackAsync(ctx, Cb.SeoOpen, ct);
                return;
            case "/status":
                await menu.ShowStatusAsync(ctx, ct);
                return;
            case "/news":
                await DispatchCallbackAsync(ctx, new CallbackData("n", "menu"), ct);
                return;
            case "/cancel":
                state.Reset(ctx.ChatId);
                var cancelled = jobs.Cancel(ctx.ChatId);
                await ui.SendAsync(ctx, cancelled > 0 ? "⛔ Скасовую поточну задачу." : "Немає активних задач. Очікування вводу скинуто.", Keyboards.Rows(Keyboards.BackToMenu), ct);
                return;
            case not null:
                await ui.SendAsync(ctx, "Не знаю такої команди. Спробуй /start", null, ct);
                return;
        }

        var current = state.Get(ctx.ChatId);
        if (ctx.IsDemo && current.Awaiting != AwaitingInput.None)
        {
            await demo.HandleTextAsync(ctx, text, current, ct);
            return;
        }

        var handler = textHandlers.FirstOrDefault(h => h.Awaiting == current.Awaiting && current.Awaiting != AwaitingInput.None);
        if (handler is not null)
        {
            await handler.HandleAsync(ctx, text, current, ct);
            return;
        }

        if (SeoHandler.TryNormalizeUrl(text, out _) && text.Contains('.') && !text.Contains(' '))
        {
            if (ctx.IsDemo)
                await demo.HandleTextAsync(ctx, text, new UserState(AwaitingInput.SeoUrl), ct);
            else
                await seo.HandleAsync(ctx, text, null, ct);
            return;
        }

        await menu.ShowMainAsync(ctx, ct);
    }

    private async Task OnCallbackAsync(ChatContext ctx, CallbackQuery query, CancellationToken ct)
    {
        if (!CallbackData.TryParse(query.Data, out var data))
        {
            await ui.AnswerCallbackAsync(query.Id, null, ct);
            return;
        }

        string? toast = null;
        try
        {
            if (data.Module == "j" && data.Action == "cancel")
                toast = jobs.Cancel(ctx.ChatId) > 0 ? "Скасовую…" : "Немає активних задач";
            else
                toast = await DispatchCallbackAsync(ctx, data, ct);
        }
        finally
        {
            await ui.AnswerCallbackAsync(query.Id, toast, ct);
        }
    }

    private async Task<string?> DispatchCallbackAsync(ChatContext ctx, CallbackData data, CancellationToken ct)
    {
        if (ctx.IsDemo && DemoHandler.Handles(data.Module))
            return await demo.HandleAsync(ctx, data, ct);

        var handler = callbackHandlers.FirstOrDefault(h => h.Module == data.Module);
        if (handler is null)
        {
            logger.LogWarning("No handler for callback {Data}", data.ToString());
            return "Ця кнопка вже не працює";
        }

        return await handler.HandleAsync(ctx, data, ct);
    }
}
