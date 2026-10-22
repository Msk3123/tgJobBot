using SystematiaBot.Bot.Routing;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SystematiaBot.Bot.UI;

/// <summary>Thin layer over the Telegram client: screens edited in place, HTML, demo banner, long-message splitting.</summary>
public sealed class BotUi(ITelegramBotClient bot, ILogger<BotUi> logger)
{
    private static readonly LinkPreviewOptions NoPreview = new() { IsDisabled = true };

    /// <summary>Shows a menu screen: edits the callback's message when possible, otherwise sends a new one.</summary>
    public async Task<int> ShowAsync(ChatContext ctx, string html, InlineKeyboardMarkup? markup, CancellationToken ct)
    {
        var text = WithBanner(ctx, html);
        if (ctx.MessageId is { } messageId && text.Length <= MessageFormatter.MaxMessageLength)
        {
            if (await TryEditAsync(ctx.ChatId, messageId, text, markup, ct))
                return messageId;
        }

        var sent = await SendAsync(ctx.ChatId, text, markup, ct);
        ctx.MessageId = sent;
        return sent;
    }

    /// <summary>Sends a new message (split by paragraphs if needed); the keyboard goes on the last part. Returns its id.</summary>
    public async Task<int> SendAsync(long chatId, string html, InlineKeyboardMarkup? markup, CancellationToken ct)
    {
        var parts = MessageFormatter.Split(html);
        Message? last = null;
        for (var i = 0; i < parts.Count; i++)
        {
            last = await bot.SendMessage(chatId, parts[i], parseMode: ParseMode.Html,
                replyMarkup: i == parts.Count - 1 ? markup : null, linkPreviewOptions: NoPreview, cancellationToken: ct);
        }

        return last!.MessageId;
    }

    public Task<int> SendAsync(ChatContext ctx, string html, InlineKeyboardMarkup? markup, CancellationToken ct) =>
        SendAsync(ctx.ChatId, WithBanner(ctx, html), markup, ct);

    public async Task<bool> TryEditAsync(long chatId, int messageId, string html, InlineKeyboardMarkup? markup, CancellationToken ct)
    {
        try
        {
            await bot.EditMessageText(chatId, messageId, html, parseMode: ParseMode.Html, replyMarkup: markup,
                linkPreviewOptions: NoPreview, cancellationToken: ct);
            return true;
        }
        catch (ApiRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        catch (ApiRequestException ex)
        {
            logger.LogDebug("Edit of message {MessageId} failed: {Error}", messageId, ex.Message);
            return false;
        }
    }

    public async Task SendDocumentAsync(long chatId, string path, string? captionHtml, InlineKeyboardMarkup? markup, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        await bot.SendDocument(chatId, InputFile.FromStream(stream, Path.GetFileName(path)), caption: captionHtml,
            parseMode: ParseMode.Html, replyMarkup: markup, cancellationToken: ct);
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken ct, bool alert = false)
    {
        try
        {
            await bot.AnswerCallbackQuery(callbackQueryId, text, showAlert: alert, cancellationToken: ct);
        }
        catch (ApiRequestException ex)
        {
            // Old queries (after a restart) cannot be answered; nothing to do about it.
            logger.LogDebug("AnswerCallbackQuery failed: {Error}", ex.Message);
        }
    }

    public async Task<ProgressMessage> StartProgressAsync(ChatContext ctx, string html, CancellationToken ct)
    {
        var id = await SendAsync(ctx, html, Keyboards.CancelJob, ct);
        return new ProgressMessage(this, ctx, id);
    }

    public static string WithBanner(ChatContext ctx, string html) => ctx.IsDemo ? $"{MessageFormatter.DemoBanner}\n{html}" : html;
}

/// <summary>A single "⏳ …" message that is edited in place, at most once every 2 seconds.</summary>
public sealed class ProgressMessage(BotUi ui, ChatContext ctx, int messageId)
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(2);
    private DateTime _lastEdit = DateTime.UtcNow;
    private string? _lastText;

    public int MessageId => messageId;

    public async Task UpdateAsync(string html, CancellationToken ct, bool force = false)
    {
        if (html == _lastText || !force && DateTime.UtcNow - _lastEdit < MinInterval)
            return;

        _lastText = html;
        _lastEdit = DateTime.UtcNow;
        await ui.TryEditAsync(ctx.ChatId, messageId, BotUi.WithBanner(ctx, html), Keyboards.CancelJob, ct);
    }

    /// <summary>Replaces the progress text with the final result (no cancel button).</summary>
    public async Task CompleteAsync(string html, InlineKeyboardMarkup? markup, CancellationToken ct)
    {
        if (!await ui.TryEditAsync(ctx.ChatId, messageId, BotUi.WithBanner(ctx, html), markup, ct))
            await ui.SendAsync(ctx, html, markup, ct);
    }
}
